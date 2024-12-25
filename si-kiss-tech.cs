/// <summary>
/// This much tech will be assembled in each assembler at a time.
/// </summary>
static readonly MyFixedPoint ASSEMBLE_BLOCK_SIZE = 100;

/// <summary>
/// This is how much of a lower tier tech much exist before assembling
/// the next higher tier tech.
///
/// 5 lower tier tech to make the next highest tier, 10 assemblers.
/// </summary>
static readonly MyFixedPoint MINIMUM_TECH = ASSEMBLE_BLOCK_SIZE * 5 * 10;

/// <summary>
/// How many minutes to wait until counting how much tech2x and tech4x
/// is in inventory.
/// </summary>
static readonly int MINUTES_BETWEEN_RECOUNT = 1;

///////////////////////////////////////////////////
// Component types and definitions. Do not modify.
///////////////////////////////////////////////////

static readonly MyItemType TECH2 = new MyItemType("MyObjectBuilder_Component", "Tech2x");
static readonly MyDefinitionId TECH2_DEF = MyDefinitionId.Parse("MyObjectBuilder_BlueprintDefinition/Tech2x");

static readonly MyItemType TECH4 = new MyItemType("MyObjectBuilder_Component", "Tech4x");
static readonly MyDefinitionId TECH4_DEF = MyDefinitionId.Parse("MyObjectBuilder_BlueprintDefinition/Tech4x");

static readonly MyDefinitionId TECH8_DEF = MyDefinitionId.Parse("MyObjectBuilder_BlueprintDefinition/Tech8x");

static readonly MyItemType FE = new MyItemType("MyObjectBuilder_Ingot", "Iron");
static readonly MyItemType SI = new MyItemType("MyObjectBuilder_Ingot", "Silicon");
static readonly MyItemType CO = new MyItemType("MyObjectBuilder_Ingot", "Cobalt");
static readonly MyItemType AG = new MyItemType("MyObjectBuilder_Ingot", "Silver");
static readonly MyItemType AU = new MyItemType("MyObjectBuilder_Ingot", "Gold");
static readonly MyItemType U = new MyItemType("MyObjectBuilder_Ingot", "Uranium");
static readonly MyItemType PT = new MyItemType("MyObjectBuilder_Ingot", "Platinum");

/// <summary>
/// Recipies for crafting tech. Defines the components that are
/// required for crafting each tech item.
/// </summary>
static readonly Dictionary<MyDefinitionId, List<MyTuple<MyItemType, MyFixedPoint>>> RECIPIES =
    new Dictionary<MyDefinitionId, List<MyTuple<MyItemType, MyFixedPoint>>> {
        {
            TECH2_DEF,
            new List<MyTuple<MyItemType, MyFixedPoint>> {
                new MyTuple<MyItemType, MyFixedPoint>(FE, 90),
                new MyTuple<MyItemType, MyFixedPoint>(SI, 80),
                new MyTuple<MyItemType, MyFixedPoint>(CO, 32),
                new MyTuple<MyItemType, MyFixedPoint>(AG, 24),
                new MyTuple<MyItemType, MyFixedPoint>(AU, 16)
            }
        },
        {
            TECH4_DEF,
            new List<MyTuple<MyItemType, MyFixedPoint>> {
                new MyTuple<MyItemType, MyFixedPoint>(TECH2, 5),
                new MyTuple<MyItemType, MyFixedPoint>(U, 10)
            }
        },
        {
            TECH8_DEF,
            new List<MyTuple<MyItemType, MyFixedPoint>> {
                new MyTuple<MyItemType, MyFixedPoint>(TECH4, 5),
                new MyTuple<MyItemType, MyFixedPoint>(PT, 10)
            }
        }
    };

//////////////////////////////////////////////
// Internal program variables, do not modify.
//////////////////////////////////////////////

/// <summary>
/// The script only runs every MINUTES_BETWEEN_RUN. This will be set to
/// the time the script will run next.
/// </summary>
DateTime nextRecount = DateTime.UtcNow;

/// <summary>
/// Every tick this is echoed to the screen. It is reset once each
/// MINUTES_BETWEEN_RUN.
/// </summary>
string currentEcho = "";

/// <summary>
/// Set if an unrecoverable error was encountered. Setting this will
/// prevent the script from running again until it is reset.
/// </summary>
string error = null;

/// <summary>
/// Assemblers on the current grid.
/// </summary>
List<IMyAssembler> assemblers = new List<IMyAssembler>();

/// <summary>
/// Inventories to check for components.
/// </summary>
List<IMyInventory> inventories = new List<IMyInventory>();

/// <summary>
/// Each move job is executed in its own tick. This contains the list
/// of jobs.
/// </summary>
Stack<MyTuple<IMyAssembler, MyDefinitionId>> jobs =
    new Stack<MyTuple<IMyAssembler, MyDefinitionId>>();

/// <summary>
/// The current tech to produce. Set every MINUTES_BETWEEN_RECOUNT.
/// </summary>
MyDefinitionId currentTech = TECH2_DEF;

/// <summary>
/// Runs once on startup.
/// </summary>
public Program() {
    List<IMyAssembler> allAssemblers = new List<IMyAssembler>();
    GridTerminalSystem.GetBlocksOfType(allAssemblers, block => block.IsSameConstructAs(Me));
    foreach (IMyAssembler assembler in allAssemblers) {
        // Skip any survival kits.
        if (assembler.BlockDefinition.SubtypeName.Contains("SurvivalKit")) {
            continue;
        }
        assemblers.Add(assembler);
    }

    List<IMyCargoContainer> cargoContainers = new List<IMyCargoContainer>();
    GridTerminalSystem.GetBlocksOfType(cargoContainers, block => block.IsSameConstructAs(Me));

    foreach (IMyCargoContainer cargoContainer in cargoContainers) {
        inventories.Add(cargoContainer.GetInventory());
    }
    foreach (IMyAssembler assembler in assemblers) {
        inventories.Add(assembler.OutputInventory);
    }

    // Main will be called every 100 ticks.
    Runtime.UpdateFrequency = UpdateFrequency.Update100;
}

/// <summary>
/// Runs every 100 ticks.
///
/// Does one, and *only* one of the following:
///
/// 1) Does a recount of all the tech2x and tech4x in inventory and
///    sets currentTech.
///
/// or
///
/// 2) Finds an assembler with an empty queue. If one is found queues
///    up 100 instances of currentTech. Only one empty assembler is
///    handled per-update. Additional assemblers will be handled on
///    subsequent updates.
/// </summary>
public void Main() {
    // Do not run if the scirpt has encountered an error.
    if (error != null) {
        Echo(error);
        return;
    }

    DateTime now = DateTime.UtcNow;
    if (now >= nextRecount) {
        // Recount is intense, only count tech once every
        // MINUTES_BETWEEN_RECOUNT.
        nextRecount = now.AddMinutes(MINUTES_BETWEEN_RECOUNT);

        RecountTech();
        Echo(currentEcho);
        return;
    }

    foreach (IMyAssembler assembler in assemblers) {
        if (assembler.IsQueueEmpty) {
            AddToQueue(assembler, currentTech);
            // Only allow one queue to be processed per tick. If there
            // are more empty queues they will be filled on the next
            // tick.
            Echo(currentEcho);
            return;
        }
    }

    Echo(currentEcho);
}

/// <summary>
/// Runs aproximately once every MINUTES_BETWEEN_RUN.
///
/// Counts the amount of tech2x and tech4x in all inventories. Sets
/// currentTech to the tech that we should currently be producing.
/// </summary>
public void RecountTech() {
    // Reset the currentEcho string, so we have fresh output. Add a
    // timestamp so it's possible to monitor the script progress.
    currentEcho = $"{DateTime.UtcNow}\n";

    MyTuple<MyFixedPoint, MyFixedPoint> techCount = GetTechCount();
    MyFixedPoint tech2Count = techCount.Item1;
    MyFixedPoint tech4Count = techCount.Item2;

    if (tech2Count < MINIMUM_TECH) {
        currentEcho += $"Making tech2, current count: {tech2Count}, minimum count: {MINIMUM_TECH}\n\n";
        currentTech = TECH2_DEF;
    } else if (tech4Count < MINIMUM_TECH) {
        currentEcho += $"Making tech4, current count: {tech4Count}, minimum count: {MINIMUM_TECH}\n\n";
        currentTech = TECH4_DEF;
    } else {
        currentEcho += "Making tech8\n\n";
        currentTech = TECH8_DEF;
    }
}

/// <summary>
/// Get the tech2x and tech4x counts.
/// </summary>
/// <returns>The Item1 is the amount of tech2x in inventory, Item2 is
/// the amount of tech4x in inventory.</returns>
public MyTuple<MyFixedPoint, MyFixedPoint> GetTechCount() {
    MyFixedPoint tech2Count = 0;
    MyFixedPoint tech4Count = 0;
    foreach (IMyInventory inventory in inventories) {
        List<MyInventoryItem> items = new List<MyInventoryItem>();
        inventory.GetItems(items);
        foreach (MyInventoryItem item in items) {
            if (item.Type == TECH2) {
                tech2Count += item.Amount;
            } else if (item.Type == TECH4) {
                tech4Count += item.Amount;
            }
        }
    }

    return new MyTuple<MyFixedPoint, MyFixedPoint>(tech2Count, tech4Count);
}

/// <summary>
/// Add ASSEMBLE_BLOCK_SIZE items of type techDef to the assemblers
/// build queue.
///
/// Moves all required components and ingots to the assemblers input
/// inventory to avoid having the assembler having to fetch the
/// components while assembling.
/// </summary>
/// <param name="assembler">The items are added to this assembler's
/// queue.</param>
/// <param name="techDef">The tech to add to the queue.</param>
public void AddToQueue(IMyAssembler assembler, MyDefinitionId techDef) {
    List<MyTuple<MyItemType, MyFixedPoint>> recipie = RECIPIES[techDef];
    currentEcho += $"Making {techDef.ToString().Split('/')[1]} in {assembler.CustomName}\n";
    assembler.Mode = MyAssemblerMode.Assembly;
    assembler.Repeating = false;
    MoveItems(recipie, assembler.InputInventory);
    assembler.AddQueueItem(techDef, ASSEMBLE_BLOCK_SIZE);
}

/// <summary>
/// Move items from any available location to the destination
/// inventory.
/// </summary>
/// <param name="recipie">The recipie contains the components and
/// amounts required to assemble a single unit of a specific type of
/// tech. Enough components from this recipie to assemble
/// ASSEMBLE_BLOCK_SIZE instances will be moved into the destination
/// inventory.</param>
/// <param name="destinationInventory">The inventory to move the
/// components into. This is intended to be an assembler's
/// InputInventory.</param>
public void MoveItems(List<MyTuple<MyItemType, MyFixedPoint>> recipie, IMyInventory destinationInventory) {
    foreach (MyTuple<MyItemType, MyFixedPoint> instruction in recipie) {
        MoveItem(destinationInventory, instruction.Item1, instruction.Item2 * ASSEMBLE_BLOCK_SIZE);
    }
}

/// <summary>
/// Move an item from any available inventory to the destination
/// inventory.
/// </summary>
/// <param name="destinationInventory">The inventory to move the items
/// to.</param>
/// <param name="itemType">The type of items to move.</param>
/// <param name="amount">The amount of items to move.</param>
/// <exception cref="Exception">Failed to move the items to the
/// inventory. This likely means that there is not enough components to
/// move, or some other unrecoverable condition. If this exception is
/// thrown it's expected that the script will terminate.</exception>
public void MoveItem(IMyInventory destinationInventory, MyItemType itemType, MyFixedPoint amount) {
    MyInventoryItem? existingItems = destinationInventory.FindItem(itemType);
    if (existingItems.HasValue) {
        amount -= existingItems.Value.Amount;
    }

    int attempts = 0;
    while (amount > 0) {
        if (++attempts > 10) {
            error = $"Too many attempts to move {itemType}";
            throw new Exception(error);
        }

        IMyInventory sourceInventory = GetBestInventory(itemType);
        MyInventoryItem? item = sourceInventory.FindItem(itemType);

        if (item.HasValue) {
            MyFixedPoint amountToMove = item.Value.Amount > amount ? amount : item.Value.Amount;
            bool success = sourceInventory.TransferItemTo(destinationInventory, item.Value, amountToMove);
            if (success) {
                amount -= amountToMove;
            }
        }

        if (amount > 0) {
            bestInventoryForItemType.Remove(itemType);
        }
    }
}

/// <summary>
/// A cache of the best locations to get specific items.
/// </summary>
Dictionary<MyItemType, IMyInventory> bestInventoryForItemType =
    new Dictionary<MyItemType, IMyInventory>();

/// <summary>
/// Get the inventory that contains the highest amount of itemType.
/// </summary>
/// <param name="itemType">The type of item to search for.</param>
/// <returns>The inventory that contains the highest amount of
/// itemType.</returns>
/// <exception cref="Exception">No inventory was found that contains
/// the requested item. If this exception is thrown it's expected that
/// the script will terminate.</exception>
public IMyInventory GetBestInventory(MyItemType itemType) {
    if (bestInventoryForItemType.ContainsKey(itemType)) {
        return bestInventoryForItemType[itemType];
    }

    IMyInventory bestInventory = null;
    MyFixedPoint bestAmount = 0;
    foreach (IMyInventory inventory in inventories) {
        MyInventoryItem? item = inventory.FindItem(itemType);
        if (item.HasValue && (bestInventory == null || item.Value.Amount > bestAmount)) {
            bestInventory = inventory;
            bestAmount = item.Value.Amount;
        }
    }

    if (bestInventory == null) {
        error = $"No more {itemType.SubtypeId} found!";
        throw new Exception(error);
    }

    bestInventoryForItemType[itemType] = bestInventory;
    return bestInventory;
}