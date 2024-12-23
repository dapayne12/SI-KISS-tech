// This much tech will be assembled in each assembler at a time.
static MyFixedPoint ASSEMBLE_BLOCK_SIZE = 100;

// This is how much of a lower tier tech much exist before assembling the next higher tier tech.
// 5 lower tier tech to make the next highest tier, 10 assemblers.
static MyFixedPoint MINIMUM_TECH = ASSEMBLE_BLOCK_SIZE * 5 * 10;

static List<IMyAssembler> assemblers = new List<IMyAssembler>();
static List<IMyInventory> inventories = new List<IMyInventory>();

MyItemType tech2 = new MyItemType("MyObjectBuilder_Component", "Tech2x");
MyDefinitionId tech2def = MyDefinitionId.Parse("MyObjectBuilder_BlueprintDefinition/Tech2x");

MyItemType tech4 = new MyItemType("MyObjectBuilder_Component", "Tech4x");
MyDefinitionId tech4def = MyDefinitionId.Parse("MyObjectBuilder_BlueprintDefinition/Tech4x");

MyDefinitionId tech8def = MyDefinitionId.Parse("MyObjectBuilder_BlueprintDefinition/Tech8x");

DateTime nextRunTime = DateTime.UtcNow;

String currentEcho = "";

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
    DateTime now = DateTime.UtcNow;
    if (now < nextRunTime) {
        Echo(currentEcho);
        return;
    }

    nextRunTime = now.AddMinutes(1);

    currentEcho = DateTime.UtcNow.ToString() + "\n";

    MyFixedPoint tech2Count = 0;
    MyFixedPoint tech4Count = 0;
    foreach (IMyInventory inventory in inventories) {
        List<MyInventoryItem> items = new List<MyInventoryItem>();
        inventory.GetItems(items);
        foreach (MyInventoryItem item in items) {
            if (item.Type == tech2) {
                tech2Count += item.Amount;
            } else if (item.Type == tech4) {
                tech4Count += item.Amount;
            }
        }
    }

    if (tech2Count < MINIMUM_TECH) {
        currentEcho += "Making tech2, current count: " + tech2Count.ToString() + "\n";
        MakeTech(tech2def);
    } else if (tech4Count < MINIMUM_TECH) {
        currentEcho += "Making tech4, current count: " + tech4Count.ToString() + "\n";
        MakeTech(tech4def);
    } else {
        currentEcho += "Making tech8\n";
        MakeTech(tech8def);
    }

    Echo(currentEcho);
}

public void MakeTech(MyDefinitionId techDef) {
    foreach (IMyAssembler assembler in assemblers) {
        if (assembler.IsQueueEmpty) {
            currentEcho += "Making " + techDef.ToString().Split('/')[1] + " in " + assembler.CustomName + "\n";
            assembler.AddQueueItem(techDef, ASSEMBLE_BLOCK_SIZE);
        }
    }
}