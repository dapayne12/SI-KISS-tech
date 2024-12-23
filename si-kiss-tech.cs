// This much tech will be assembled in each assembler at a time.
static MyFixedPoint ASSEMBLE_BLOCK_SIZE = 100;

// This is how much of a lower tier tech much exist before assembling the next higher tier tech.
// 5 lower tier tech to make the next highest tier, 10 assemblers.
static MyFixedPoint MINIMUM_TECH = ASSEMBLE_BLOCK_SIZE * 5 * 10;

// How many minutes to wait between each run.
static int MINUTES_BETWEEN_RUN = 1;

static MyItemType TECH2 = new MyItemType("MyObjectBuilder_Component", "Tech2x");
static MyDefinitionId TECH2_DEF = MyDefinitionId.Parse("MyObjectBuilder_BlueprintDefinition/Tech2x");

static MyItemType TECH4 = new MyItemType("MyObjectBuilder_Component", "Tech4x");
static MyDefinitionId TECH4_DEF = MyDefinitionId.Parse("MyObjectBuilder_BlueprintDefinition/Tech4x");

static MyDefinitionId TECH8_DEF = MyDefinitionId.Parse("MyObjectBuilder_BlueprintDefinition/Tech8x");

static MyItemType FE = new MyItemType("MyObjectBuilder_Ingot", "Iron");
static MyItemType SI = new MyItemType("MyObjectBuilder_Ingot", "Silicon");
static MyItemType CO = new MyItemType("MyObjectBuilder_Ingot", "Cobalt");
static MyItemType AG = new MyItemType("MyObjectBuilder_Ingot", "Silver");
static MyItemType AU = new MyItemType("MyObjectBuilder_Ingot", "Gold");
static MyItemType U = new MyItemType("MyObjectBuilder_Ingot", "Uranium");
static MyItemType PT = new MyItemType("MyObjectBuilder_Ingot", "Platinum");

static Dictionary<MyDefinitionId, List<MyTuple<MyItemType, MyFixedPoint>>> RECIPIES = new Dictionary<MyDefinitionId, List<MyTuple<MyItemType, MyFixedPoint>>> {
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

DateTime nextRunTime = DateTime.UtcNow;

string currentEcho = "";
string error = null;

List<IMyAssembler> assemblers = new List<IMyAssembler>();
List<IMyInventory> inventories = new List<IMyInventory>();

Stack<MyTuple<IMyAssembler, MyDefinitionId>> jobs = new Stack<MyTuple<IMyAssembler, MyDefinitionId>>();

public Program()
{
    GridTerminalSystem.GetBlocksOfType(assemblers, block => block.IsSameConstructAs(Me));

    List<IMyCargoContainer> cargoContainers = new List<IMyCargoContainer>();
    GridTerminalSystem.GetBlocksOfType(cargoContainers, block => block.IsSameConstructAs(Me));

    foreach (IMyCargoContainer cargoContainer in cargoContainers) {
        inventories.Add(cargoContainer.GetInventory());
    }
    foreach (IMyAssembler assembler in assemblers) {
        inventories.Add(assembler.OutputInventory);
    }

    Runtime.UpdateFrequency = UpdateFrequency.Update100;
}

public void Main()
{
    if (error != null) {
        Echo(error);
        return;
    }

    if (jobs.Count > 0) {
        MyTuple<IMyAssembler, MyDefinitionId> job = jobs.Pop();
        AddToQueue(job.Item1, job.Item2);
        Echo(currentEcho);
        return;
    }

    DateTime now = DateTime.UtcNow;
    if (now < nextRunTime) {
        Echo(currentEcho);
        return;
    }

    nextRunTime = now.AddMinutes(MINUTES_BETWEEN_RUN);

    currentEcho = DateTime.UtcNow.ToString() + "\n";

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

    if (tech2Count < MINIMUM_TECH) {
        currentEcho += "Making tech2, current count: " + tech2Count.ToString() + "\n";
        MakeTech(TECH2_DEF);
    } else if (tech4Count < MINIMUM_TECH) {
        currentEcho += "Making tech4, current count: " + tech4Count.ToString() + "\n";
        MakeTech(TECH4_DEF);
    } else {
        currentEcho += "Making tech8\n";
        MakeTech(TECH8_DEF);
    }

    Echo(currentEcho);
}

public void MakeTech(MyDefinitionId techDef) {
    foreach (IMyAssembler assembler in assemblers) {
        if (assembler.IsQueueEmpty) {
            jobs.Push(new MyTuple<IMyAssembler, MyDefinitionId>(assembler, techDef));
        }
    }
}

public void AddToQueue(IMyAssembler assembler, MyDefinitionId techDef) {
    List<MyTuple<MyItemType, MyFixedPoint>> recipie = RECIPIES[techDef];
    currentEcho += "Making " + techDef.ToString().Split('/')[1] + " in " + assembler.CustomName + "\n";
    MoveItems(recipie, assembler.InputInventory);
    assembler.AddQueueItem(techDef, ASSEMBLE_BLOCK_SIZE);
}

public void MoveItems(List<MyTuple<MyItemType, MyFixedPoint>> recipie, IMyInventory destinationInventory) {
    foreach (MyTuple<MyItemType, MyFixedPoint> instruction in recipie) {
        MoveItem(destinationInventory, instruction.Item1, instruction.Item2 * ASSEMBLE_BLOCK_SIZE);
    }
}

public void MoveItem(IMyInventory destinationInventory, MyItemType itemType, MyFixedPoint amount) {
    MyInventoryItem? existingItems = destinationInventory.FindItem(itemType);
    if (existingItems.HasValue) {
        amount -= existingItems.Value.Amount;
    }

    int attempts = 0;
    while (amount > 0) {
        if (++attempts > 10) {
            error = "Too many attempts to move " + itemType.ToString();
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

Dictionary<MyItemType, IMyInventory> bestInventoryForItemType = new Dictionary<MyItemType, IMyInventory>();
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
        error = "No more " + itemType.SubtypeId + " found!";
        throw new Exception(error);
    }

    bestInventoryForItemType[itemType] = bestInventory;
    return bestInventory;
}