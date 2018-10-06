/********************************************
	Ore(?) Transport Ship
*********************************************/
/*
    Remastered the whole f******* thing !
    * changed the Freightconatiner with ISI's percent
    * Started to use CustomData of PB
    * Changed Battery status
    * Changed CustomData references
    * Changed the way to find the Connector
    * Changed GotoBase
    * Changed GotoMine
    * Changed Customdata <-> Programstatus
    * Adapted the main Programstatus Loop
    * Implemented manual control
    * Implemenied Warning Lights
    * Implemented efficency in the runs --> Depotmaster ???
    * Changing OreTRansfer to acount for connectors
    * Implementing Antenna system
    * Checking the Empty run stuff -> Should only go back if his power is low
     -> maybe this is a task for depotmaster
    * base has a piston -> waiting for it go up -> Timer Block Reverse
     -- only at base and only by taking off not landing(Sensor @ base)
    * We Should be able to define either Alfa OR Bravo as @Base
     --> TODO implementing a "choosing" procedure
    * TODO Oretransfer use another system than Checkmine ???

 */
public Program() {
    Runtime.UpdateFrequency = UpdateFrequency.Update10;
    string[] storedData = Storage.Split(';');

    if(storedData.Length >= 1) {
        ProgramStatus = storedData[0];
    }
    
    if(storedData.Length >= 2) {
        Position = storedData[1];
    }
}

public void Save() {
    Storage = string.Join(";",
        ProgramStatus ?? "@Base",
        Position ?? "@Base");
}

// Fancy stuff
const string VERSION = "1.21";
public static List<char> SLIDER_ROTATOR = new List<char>(new char[] { '-', '\\', '|', '/'}); 
public DisplaySlider rotator = new DisplaySlider(Program.SLIDER_ROTATOR); 

// Naming blocks
const string DRONENAME = "Drone"; // should be unique
const string LCDNAME = DRONENAME + "LCD";

const string FREIGHTCONTAINER = DRONENAME + "Container";
const string DRONEBATTERY = DRONENAME + " Battery";

const string SAMTag = "[SAMDRONE]";
const string DRONECONNECTOR = DRONENAME + "Connector " + SAMTag ;
const string DRONESAM = "PB "+ DRONENAME + " " + SAMTag;
const string THRUSTERGROUPNAME = DRONENAME + " Thrusters";
const string DRONEWARNINGLIGHT = DRONENAME + " Warning Light";

const string MINECONTAINERPATTERN =  "Mine";
const string MINECONTAINER = "LContainer MineCar";

const string TIMERBLOCK= "TB - Piston ctrl Drone connection";

string HomeSweetHome = "Alfa";

//Minuma Levels in percentages
int MinimumPower = 80;
int MinimumStored = 60; // to start flying that is or wait for more
int MaximumStored = 99; // At what level the freightcontainer is considered full
// int MaximumEmptyRuns = 5; // at which point does the drone gives up ?

// Booleans and stuff
bool ProgramRunning = false;
bool DroneConnected = false;
bool StorageDone = false;
bool PowerOk = false;
bool NoStone = true;
bool YouHaveControl = false;
bool TimerTriggered = false;

// Antenna System
const string AntennaName = "Antenna Drone";
// Carier is what the DM need
const string SENDMESSAGEHEADER = "Carier:" + DRONENAME; // should be unique name
int SendTimer=0;

string ProgramStatus = "Euh";
string OldProgramStatus = "Euh";
string Position = "Error";

public IMyCargoContainer OreContainer;
public IMyShipConnector DroneConnector;
public IMyProgrammableBlock PCSam;
public IMyInteriorLight DroneWarningLight;

List<IMyTerminalBlock> MineOreContainers = new List<IMyTerminalBlock>();
List<IMyTerminalBlock> FreightContainers = new List<IMyTerminalBlock>();
List<IMyTerminalBlock> DroneConnectors = new List<IMyTerminalBlock>();
List<IMyTerminalBlock> PB_blocks = new List<IMyTerminalBlock>();
List<IMyTerminalBlock> Dronethrusters = new List<IMyTerminalBlock>();
List<IMyTerminalBlock> DroneWarningLights= new List<IMyTerminalBlock>();

string Message = "If this shows: something is wrong\n";

string OldMessage = "Nothing";
string NewMessage = "Nothing";

public void Main(string argument, UpdateType updateSource)
{
    string ShowRunning = (ProgramRunning) ? " [Auto On]" : " [Auto Off]";
    Message = " " + Me.CustomName + " " + VERSION + ShowRunning + "\n";

    // We need a PC with SAM -> with connection ?
    if (!CheckSAM())
    {
        //  |-] No SAM available? \n or more than one SAM\n
        Message += " |-] No " + DRONESAM + " found ?\n";
        ShowText (Message, LCDNAME, true);        
        return;        
    }

    // read the customdata of ... ourself ?
    CheckCustomData();

    // Echo ("ProgramStatus: " + ProgramStatus + "\n");
    // DEBUG
    // Message += " Position: " + Position + "\n";
    
    // DEBUG
    // Message += " Action: " + ProgramStatus + "\n";

    // could come from DepotMaster - Base Antenna
    if (argument.Length > 0)
    {
        // DEBUG
        // Message +="Argument: " + argument + "\n";

        if (argument.Contains("DepotMaster")&&(!YouHaveControl))
        {
            NewMessage = argument;
            
            var parts = argument.Split('=');
            string MasterCommando = parts[1];

            if (MasterCommando == "Fly Drone")
            {
                ProgramRunning = true;
            }

            if (MasterCommando == "Fly Home")
            {
                ProgramRunning = false;
                ProgramStatus = "Reset";
            }
        }

        switch (argument.ToLower())
        {
            case "reset":
                ProgramStatus = "Reset";
                ProgramRunning = false;
                break;
            case "stop":
                ProgramStatus = "Stopping"; // Manual control
                ProgramRunning = false;
                break; 
            case "run":
                ProgramRunning = true;
                break;
            case "manual": // for flying Mannually without interference of DPM
                YouHaveControl = !YouHaveControl;
                if (YouHaveControl) 
                {
                    OldProgramStatus = ProgramStatus;
                    ProgramStatus = "Manual";                             
                }
                else
                {
                    if(OldProgramStatus == "Euh") OldProgramStatus = "Reset";
                    ProgramStatus = OldProgramStatus;
                }
       
                break;
            default:
                break;
        }
    }

    if(NewMessage != OldMessage)
    {
        if (NewMessage != "Nothing" ) OldMessage = NewMessage;
    }

    if (YouHaveControl)
    {
        Message += " I am al yours\n";
    }
    else
    {
        Message +=  " " + OldMessage + "\n";
    }
    Message += " Commands: Run, Reset, Stop, Manual ...\n";

    Message += " Connection:";
    GridTerminalSystem.SearchBlocksOfName(DRONECONNECTOR, DroneConnectors, c => c.HasInventory);
    if(DroneConnectors.Count > 0)
    {
        DroneConnector = DroneConnectors[0] as IMyShipConnector;

        // We need a connector & its inventory
        int ConnectorStatus = CheckConnector();
        switch (ConnectorStatus)
        {
            case -1:
                // Connector not found or not usable\n
                Message += " |-[ ?\n";
                DroneConnected = false;
                break;
            case 0:
                // this is not neccessarily if we are flying ! --> TODO
                Message += " °-|\n";
                DroneConnected = false;            
                break;
            case 1:
                // SAM Missed this or we are Manual
                Message += " °-|<>|-° \n";
                DroneConnected = false;            
                break;
            case 2:
                Message += " :-][-:\n";
                DroneConnected = true;
                break;
            default:
                Message += " --> Check Connector\n Correct Name ?\n";
                break;
        }
    }
    else
    {
        Message += "Can not find: " + DRONECONNECTOR + " on this grid\n";
        ShowText (Message, LCDNAME, true);        
        return;                
    }

    // We need container ( inventory and contenance)
    Message += "\n Load " + FreightContainers.Count + " containers \n";
    // Containerstatus is a percentage 0-100 that becomes 0-20 char + 2
    int ContainerStatus = CheckContainer();
    StorageDone = false;
    if ( ContainerStatus >= MinimumStored ) StorageDone = true;
    string ShowContainerLevel = ContainerStatus.ToString();
    string firstline = new String('I', ContainerStatus/5);
    string secondline = new String('.', 20-(ContainerStatus/5));
    string fillline = "[" + firstline + secondline + "] ";
    Message += " " + fillline + ShowContainerLevel + "%\n";

    Message += " Batteries \n";
    int PowerStatus = Checkbatteries();
    // Echo ("Powerstatus : " + PowerStatus + "\n");
    if (PowerStatus >= MinimumPower ) PowerOk = true;
    string ShowPowerLevel = PowerStatus.ToString();
    firstline = new String('I', PowerStatus/5);
    secondline = new String('.', 20-(PowerStatus/5));
    fillline = "[" + firstline + secondline + "] ";
    Message += " " + fillline + ShowPowerLevel + "%\n";
 
    Message += "\n Thrusters: ";
    if(Checkthrusters()) 
    {
        Message += "[ON]\n";
    }
    else
    {
        Message += "[OFF]\n";
    }

    if ((ProgramRunning)&&(!YouHaveControl)) WarningLights (false); 

    /********************
     Programstatus Loop
    **********************/

    switch (ProgramStatus)
    {
        case "Manual":
            Message += " You have control\n";        
            break;
        case "Stopping":
            ProgramRunning = false;
            WarningLights (true);
            break;
        case "Reset":
            ProgramRunning = false;
            WarningLights (true); 
            
            if (Position.ToLower().Contains("destination")) PCSam.TryRun("NAV STOP");

            if (Position == "@Base") 
            {
                if (!DroneConnected) 
                {
                    Landing();
                    break;
                }
                
                TimerTriggered = false; 
                Message += " well ... I am back\n";
                SendStatus("Idle");                
                break; 
            }

            if (Position == "§Base") 
            {
                Landing();
                break; 
            }

            if (Position == "#Base") 
            {
                Message += " Resetting " + rotator.GetString() + "\n";
                SendStatus("Resetting");
                // TimerTriggered = false;                                  
                break; 
            }            
            
            if(Position != ">Base")
            {
                GotoBase();
                break;
            }
            else
            {
                TakeOff();
            }
            
            break;
        case "@Destination":
           
            if (!DroneConnected) 
            {
                Landing();
                break;
            }

            if (CheckMine() > 0 )
            {
                // still something left in mine
                // check room
                if ( ContainerStatus < MaximumStored )
                {
                    OreTransfer();
                }
                else
                {
                    // freightcontainer is full
                    if (PowerOk) GotoBase();
                }
                
                // RunNumber = 0;
                // EmptyRuns = 0;
                break;   
            }

            // there is nothing at the mine
            // Do we wait ? and until when ?
            if (StorageDone)
            {
                //We have a ore -> going to base
                if (PowerOk) GotoBase();
                break;                
            }

            if (!PowerOk)
            {
                GotoBase();
                SendStatus("PowerLow"); 
            }
            else
            {
                Message += " Waiting: " + rotator.GetString();
                SendStatus("Waiting"); 
            } 
            break;
        case ">Destination":
            TakeOff();
            SendStatus("TakingOff");               
            break;
        case "#Destination":
            Message += " " + rotator.GetString() + " :-> Out\n";
            SendStatus("FlyingOut");                
            break;
        case "§Destination":
            Landing();
            break;
        case "@Base":
            // Landing could not be complete
            // because the base has a piston on the connector
            if (!DroneConnected) 
            {
                Landing();
                break;
            }

            if (ProgramRunning)
            {
                // Ore master should get all ore if containers/connectors have Remorque in the name
                // Echo("Containerstatus: " + ContainerStatus + "\n");

                if ( ContainerStatus == 0 )
                {
                    if (PowerOk) 
                    {
                        GotoMine(); // aka Destination
                    }
                    else
                    {
                        SendStatus("Texaco");
                    }
                }
            }
            break;
        case ">Base":
            TakeOff();
            break;
        case "#Base":
            Message += " Home " + rotator.GetString() + "\n";
            // TimerTriggered = false;           
            SendStatus("CommingHome");    
            break;
        case "§Base":
            Landing();
            break;
    }

    Save();


    ShowText (Message, LCDNAME, true);
}

/***************************
    Subroutines
 ***************************/

/**************
    Containers
 **************/
public int CheckContainer()
{
    float StoredVolume  = 0;
    float MaxVolume = 0;  
    int OreContainerStatus = 0;
    int OreContainersStatus = 0;
    string ShowContainerStatus = "Error";

    GridTerminalSystem.SearchBlocksOfName(FREIGHTCONTAINER, FreightContainers, c => c.HasInventory);

    if (FreightContainers.Count < 1 ) 
    {
        Message += "Can not find: " + FREIGHTCONTAINER + " on the grid\n checked the name?\n";
        return 0;
    }
    foreach ( IMyCargoContainer Container  in FreightContainers )
    {
        IMyInventory ThisStock = Container.GetInventory(0);
        
        StoredVolume += (float)ThisStock.CurrentVolume;
        MaxVolume += (float)ThisStock.MaxVolume;

		string oldName = Container.CustomName;
		string newName = "";

		var findPercent = System.Text.RegularExpressions.Regex.Match(oldName, @"\(\d+\.?\d*\%\)").Value;

		if (findPercent != "") {
			newName = oldName.Replace(findPercent, "").TrimEnd(' ');
		} else {
			newName = oldName;
		}

		OreContainerStatus = Convert.ToInt32(PercentOf((double)ThisStock.CurrentVolume, (double)ThisStock.MaxVolume));
        ShowContainerStatus = OreContainerStatus.ToString();

        // Echo ("Percent: " + ShowContainerStatus);
		newName += " (" + ShowContainerStatus + "%)";
		newName = newName.Replace("  ", " ");

		if (newName != oldName) Container.CustomName = newName;

    }

	OreContainersStatus = Convert.ToInt32(PercentOf((double)StoredVolume, (double)MaxVolume));
    return OreContainersStatus;
}

// Check the inventory of the Mine
// F**** ! Connectors have inventory too ! they are counted as well
// and ... stone ?
public float CheckMine()
{
    float AmountOres = 0f;

    GridTerminalSystem.SearchBlocksOfName(MINECONTAINERPATTERN, MineOreContainers, c => c.HasInventory);
    if (MineOreContainers == null ) return 0;

    for (int i = 0; i < MineOreContainers.Count; i++)
    {

        var MineContainer = MineOreContainers[i];
     
        IMyInventory ThisStock = MineContainer.GetInventory(0);    
        findOre(ThisStock);
        foreach (var OreType in OreTypes)
        {
            if ((OreType != "Stone") && (NoStone))
            {
                AmountOres += countItem(ThisStock, OreType);
            }
        }
    }

    return AmountOres;
}

// Connectors are a pain in the a****
void OreTransfer()
{
    float AmountOres = 0f;
    float TransferAmount = 0f;

	GridTerminalSystem.SearchBlocksOfName(MINECONTAINER, MineOreContainers, c => c.HasInventory);

    if (MineOreContainers == null) return;

    // find a container with space
    IMyInventory FreightStock = FindSpace();
    if (FreightStock == null) return; // no space left on the drone

    for ( int i=0; i < MineOreContainers.Count; i++ )
    {
        if (MineOreContainers[i].CustomName.Contains("Connector"))
        {
            IMyShipConnector ThisContainer = MineOreContainers[i] as IMyShipConnector;
            IMyInventory OreStock = ThisContainer.GetInventory(0);

            findOre(OreStock);
            foreach (var Oretype in OreTypes)
            {
                TransferAmount = countItem(OreStock, Oretype); 
                if (TransferAmount > 0) MyTransfer(OreStock, FreightStock, "Ore", Oretype, TransferAmount);
            }
        }
        else
        {
            // real Cargo containers ;-)
            IMyCargoContainer MineOreContainer = MineOreContainers[i] as IMyCargoContainer;       
            IMyInventory MineStock = MineOreContainer.GetInventory(0);

            findOre(MineStock);
            foreach (var Oretype in OreTypes)
            {
                AmountOres += countItem(MineStock, Oretype);
                // Echo ("found: " + AmountOres + " " +  Oretype + "\n");

                TransferAmount = countItem(MineStock, Oretype);
                //DEBUG
                // Echo ("transfer: " + TransferAmount + " \n");

                if (TransferAmount > 0) MyTransfer(MineStock, FreightStock, "Ore", Oretype, TransferAmount);
            }
        }
    }
}

public IMyInventory FindSpace()
{
    GridTerminalSystem.SearchBlocksOfName(FREIGHTCONTAINER, FreightContainers, c => c.HasInventory);
   // find a container with space
    
    foreach ( IMyCargoContainer FreightContainer in FreightContainers )
    {
        IMyInventory FreightStock = FreightContainer.GetInventory(0);
        if ((float)FreightStock.CurrentVolume < (float)FreightStock.MaxVolume * 0.99 ) 
        {
            return FreightStock;
        }
        
    }

    return null;
}

float countItem(IMyInventory inv, string itemSubType)
{
    var items = inv.GetItems();
    float total = 0.0f;
    for(int i = 0; i < items.Count; i++)
    {
        if(items[i].Content.TypeId.ToString().EndsWith("Ore") && items[i].Content.SubtypeId.ToString() == itemSubType)
        {
            total += (float)items[i].Amount;
        }
    }
    return total;
}

void MyTransfer(IMyInventory FromStock, IMyInventory ToStock, string type, string subType, float amount)
{
    var items = FromStock.GetItems();
    float left = amount;
    for(int i = items.Count - 1; i >= 0; i--)
    {
        if(left > 0 && items[i].Content.TypeId.ToString().EndsWith(type) && items[i].Content.SubtypeId.ToString() == subType)
        {
            if((float)items[i].Amount > left)
            {
                // transfer remaining and break
                FromStock.TransferItemTo(ToStock, i, null, true, (VRage.MyFixedPoint)amount);
                left = 0;
                break;
            }
            else
            {
                left -= (float)items[i].Amount;
                // transfer all
                FromStock.TransferItemTo(ToStock, i, null, true, null);
            }
        }
    }
}

/**************
   Connector
**************/
public int CheckConnector()
{
	int Status = -1;
    // Unconnected	0	This connector is not connected to anything, nor is it near anything connectable.
    // Connectable	1	This connector is currently near something that it can connect to.
    // Connected	2	This connector is currently connected to something.
    // IMyShipConnector DroneConnector = GridTerminalSystem.GetBlockWithName(DRONECONNECTOR) as IMyShipConnector;
   
    if(DroneConnector == null){return -1;}
    
    MyShipConnectorStatus DroneConnectorStatus = DroneConnector.Status;

    switch (DroneConnectorStatus)
    {
        case MyShipConnectorStatus.Connected:
            Status = 2;
            break;
        case MyShipConnectorStatus.Connectable:
            Status = 1;
            break;
        case MyShipConnectorStatus.Unconnected:
            Status = 0;
            break;
        default:
            Status = -1;
            break;
    }

    return Status;
}

public void ConnectConnector()
{
    // IMyShipConnector DroneConnector = GridTerminalSystem.GetBlockWithName(DRONECONNECTOR) as IMyShipConnector;
    if(DroneConnector != null)
    {
        DroneConnector.Connect();
    }
    else
    {
        Message += " |-[ could not find " + DRONECONNECTOR + " \n";
    }
}

public void DisConnectConnector()
{
    // IMyShipConnector DroneConnector = GridTerminalSystem.GetBlockWithName(DRONECONNECTOR) as IMyShipConnector;
    if(DroneConnector != null)
    {
        DroneConnector.Disconnect();
    }
    else
    {
        Message += " |-[ could not find " + DRONECONNECTOR + " \n";
    }
}

/*****************
    batteries
*******************/

public int Checkbatteries()
{
    float StoredEnergy  = 0;
    float MaxEnergy = 0;  
    int BatteriesStatus = 0;
    int BatteryStatus = 0; 
  
    List<IMyTerminalBlock> PB_blocks = new List<IMyTerminalBlock>();
    GridTerminalSystem.SearchBlocksOfName(DRONEBATTERY, PB_blocks, b => b is IMyBatteryBlock);

    // Echo ("Found " + PB_blocks.Count + " batteries\n");

    if (PB_blocks.Count < 1 )
    {
        Message += " can not find: " + DRONEBATTERY + " on the grid\n Check the name(s)\n";
        return 0;
    }

    foreach ( IMyBatteryBlock Battery in PB_blocks )
    {
        StoredEnergy += (float)Battery.CurrentStoredPower;
        MaxEnergy += (float)Battery.MaxStoredPower;  

    	string oldName = Battery.CustomName;
	    string newName = "";

	    BatteryStatus = Convert.ToInt32(PercentOf((double)Battery.CurrentStoredPower, (double)Battery.MaxStoredPower));
        string ShowBatteryStatus = BatteryStatus.ToString();

		var findPercent = System.Text.RegularExpressions.Regex.Match(oldName, @"\(\d+\.?\d*\%\)").Value;

		if (findPercent != "") {
			newName = oldName.Replace(findPercent, "").TrimEnd(' ');
		} else {
			newName = oldName;
		}

	    newName += " (" + ShowBatteryStatus + "%)";
	    newName = newName.Replace("  ", " ");

	    if (newName != oldName) Battery.CustomName = newName;
    }

    /*
    Echo ("CurrentPower: " + StoredEnergy);
    Echo ("MaxPower: " + MaxEnergy);

    Echo ("My counting: " + ((100/MaxEnergy)*StoredEnergy).ToString() );
    */

	BatteriesStatus = Convert.ToInt32(PercentOf((double)StoredEnergy, (double)MaxEnergy));
    return BatteriesStatus;
}

/*********
    SAM
**********/

// ! We should make this with CustomData ...

public bool CheckSAM()
{
    // List<IMyTerminalBlock> PB_blocks = new List<IMyTerminalBlock>();
    PB_blocks.Clear();
    GridTerminalSystem.SearchBlocksOfName(DRONESAM, PB_blocks, b => b is IMyProgrammableBlock);
 
    if (PB_blocks == null) return false;
    if (PB_blocks.Count < 1) return false;
 
    PCSam = PB_blocks[0] as IMyProgrammableBlock;

    return true;   
}

public void TakeOff()
{
    // Thrusters on
    ThrustersOn();

    CheckConnector();
    // check connector
    if (DroneConnected) 
    {
        DisConnectConnector();
    }
    else
    {
        // Only @ the base
        if(!TimerTriggered) TriggerTimer();
    }

    // fly
    PCSam.TryRun("NAV START");    

}

public void Landing()
{
    // Maybe later from a distance now we use a sensor
    // if(!TimerTriggered) TriggerTimer();
    
    // check connector
    CheckConnector();
    if (!DroneConnected) 
    {
        ConnectConnector();
    }
    else
    {
        ThrustersOff(); 
    }
}

public void GotoBase()
{
    // Get SAM on the correct location
    if (Position != ">Base")
    {
        PCSam.TryRun("DOCK PREV");
        return;
    }
}

public void GotoMine()
{
    // Get SAM on the correct location
    if (Position != ">Destination")
    {
        PCSam.TryRun("DOCK NEXT");
        return;
    }
}


/********************
   Item Processing
 *******************/

List<string> OreTypes = new List<string>();

public void findOre(IMyInventory inv)
{
    OreTypes.Clear();
    var items = inv.GetItems();
    for(int i = 0; i < items.Count; i++)
    {
        if(items[i].Content.TypeId.ToString().EndsWith("Ore"))
        {
            OreTypes.Add(items[i].Content.SubtypeId.ToString());
        }
    }

}

void transfer(IMyInventory FromStock, IMyInventory ToStock, string type, string subType, float amount)
{
    var items = FromStock.GetItems();
    float left = amount;
    for(int i = items.Count - 1; i >= 0; i--)
    {
        if(left > 0 && items[i].Content.TypeId.ToString().EndsWith(type) && items[i].Content.SubtypeId.ToString() == subType)
        {
            if((float)items[i].Amount > left)
            {
                // transfer remaining and break
                FromStock.TransferItemTo(ToStock, i, null, true, (VRage.MyFixedPoint)amount);
                left = 0;
                break;
            }
            else
            {
                left -= (float)items[i].Amount;
                // transfer all
                FromStock.TransferItemTo(ToStock, i, null, true, null);
            }
        }
    }
}

/****************   
    LCD   
******************/   
    
public void ShowText(string Tekst, string LCDName = "Testing",  bool RepeatEcho = false)     
{     
    List<IMyTerminalBlock> MyLCDs = new List<IMyTerminalBlock>();     
    GridTerminalSystem.SearchBlocksOfName(LCDName, MyLCDs, block => block is IMyTextPanel);
   
    if ((MyLCDs == null) || (MyLCDs.Count == 0))     
    {     
		Echo( "|-0 No LCD-panel found with " + LCDName + "\n" );     
        Echo(Tekst);     
    }     
    else     
    {     
        if (RepeatEcho)
        {
            Echo(Tekst);  // Control
        }

        for (int i = 0; i < MyLCDs.Count; i++)     
        {     
     		IMyTextPanel ThisLCD = GridTerminalSystem.GetBlockWithName(MyLCDs[i].CustomName) as IMyTextPanel;     
			if (ThisLCD == null)     
			{     
				Echo("°-X LCD not found? \n");     
			}     
			else     
			{     
                // test -> Echo("Using " + MyLCDs.Count + " LCDs\n" );
 
                ThisLCD.WritePublicText(Tekst, false);     
                ThisLCD.ShowPublicTextOnScreen();    
            }    
    	}     
    }     
}


/*******************
   ThrusterControl
 *******************/

public bool Checkthrusters()
{
    
    IMyBlockGroup ThrusterGroup = GridTerminalSystem.GetBlockGroupWithName(THRUSTERGROUPNAME);

    if (ThrusterGroup == null)
    {
        Message += "Group " + THRUSTERGROUPNAME + " not found\n";
        return false;
    }
    
    // Echo($"{ThrusterGroup.Name}:");

    ThrusterGroup.GetBlocks(Dronethrusters);
    
    for (int i = 0; i < Dronethrusters.Count; i++)
    {
        IMyThrust Thruster = Dronethrusters[i] as IMyThrust;
        if (!Thruster.Enabled) return false;
    }

    return true;
}

public void ThrustersOn()
{
    Echo("Number: " + Dronethrusters.Count + "\n");
    for (int i = 0; i < Dronethrusters.Count; i++)
    {
        IMyThrust Thruster = Dronethrusters[i] as IMyThrust;
        Thruster.Enabled = true;
    }
}

public void ThrustersOff()
{
    for (int i = 0; i < Dronethrusters.Count; i++)
    {
        IMyThrust Thruster = Dronethrusters[i] as IMyThrust;

        Thruster.Enabled = false;
    }
}

/**************
    Containers
 **************/

public void CheckCustomData()
{
    // My own PB is Me
    var PCSamDataLines = PCSam.CustomData.Split('\n');

    // DEBUG
    // Message += "Sams CustomData: " + PCSam.CustomData + "\n";

    foreach (var line in PCSamDataLines) 
    {
		if (!line.Contains("=")) continue;
		var lineContent = line.Split('=');
        string Designation = "?";
        Position = "Error";

        // DEBUG
        // Message += "Customdata[0]: " + lineContent[0] + "\n";
        
        switch (lineContent[0].ToLower())
        {
            case "docks":
                Message += "No docks configured in SAM\n";
                break;
            case "arrived":
                // "NAV STOP"
                Designation = "@";
                break;
            case "new destination":
                // DOCK NEXT & DOCK PREV
                Designation = ">";
                break;
            case "flying destination":
                // "NAV START"
                Designation = "#";
                break;
            case "stopped destination":
                // "Stopping navigation"
                Designation = "§";
                break;
            default:
                break;
        }
    
        // DEBUG
        // Message += "Designation: " + Designation + "\n";
        // Message += "Customdata[1]: " + lineContent[1] + "\n";
              
        if((lineContent[1]==HomeSweetHome)||(lineContent[1].ToLower()==HomeSweetHome))
        {
            Position = Designation + "Base";
        }
        else
        {
            Position = Designation + "Destination";
        }

        if(Designation == "?")
        {
            Position = "not Defined";
            ProgramStatus = "Manual";
            ProgramRunning = false;
            YouHaveControl = true;
        }
    }

    string NewData = "";
    Me.CustomData = "";

    if ((ProgramRunning)&&(!YouHaveControl)) ProgramStatus = Position;

    NewData = "Status=" + ProgramStatus + "\n";
    NewData += "Position=" + Position + "\n";
    // copying to own PB        
    Me.CustomData = NewData;
    
    // DEBUG
    // Message += "NewCustomData: " + NewData + "\n";
}

public void WarningLights (bool LightOn=false)
{

    GridTerminalSystem.SearchBlocksOfName(DRONEWARNINGLIGHT, DroneWarningLights, block => block is IMyInteriorLight);

    if (DroneWarningLights.Count < 1) 
    {
        Message += " |-[ Warning Lights ?\n";
        return;
    }

    foreach (IMyInteriorLight WLight in DroneWarningLights)
    {
        // on
        if (LightOn) 
        {
            WLight.GetActionWithName("OnOff_On").Apply(WLight);
        }
        else
        {
            // off
            WLight.GetActionWithName("OnOff_Off").Apply(WLight);
        } 
    }
      
    return;

}

/***************
 antenna system
***************/
List<IMyTerminalBlock> Antennas = new List<IMyTerminalBlock>();
public bool SendStatus(string SendMessage="Nothing to report")
{
    SendTimer++;
    if(SendTimer<15) return false;
    SendTimer=0;

    GridTerminalSystem.SearchBlocksOfName(AntennaName, Antennas, block => block is IMyRadioAntenna);

    if ((Antennas == null)||(Antennas.Count == 0))
    {
        Message += " There are no " + AntennaName + " antennas on the grid\n No messages will be send\n";
        return false;
    }

    IMyRadioAntenna Antenna = Antennas[0] as IMyRadioAntenna;

    bool sendCall = Antenna.TransmitMessage(SENDMESSAGEHEADER + "=" + SendMessage, MyTransmitTarget.Owned);

    if (!sendCall)
    {
        Message += " --> Failed to call the Base\n";
    }

    return sendCall;
}

/**************
  Fancy stuff 
 **************/

public class DisplaySlider 
{ 
    public List<char> displayList; 
    public DisplaySlider(List<char> l) 
    { 
        this.displayList = new List<char>(l); 
    } 
    public string GetString() 
    { 
        this.displayList.Move(this.displayList.Count() - 1, 0); 
        return this.displayList.First().ToString(); 
    } 
} 

public static double PercentOf(double numerator, double denominator)
{
	double percentage = Math.Round(numerator / denominator * 100, 1);
	if (denominator == 0) {
		return 0;
	} else {
		return percentage;
	}
}

public void TriggerTimer()
{
    IMyTimerBlock timerBlock = GridTerminalSystem.GetBlockWithName(TIMERBLOCK) as IMyTimerBlock;
    if(timerBlock != null){
        timerBlock.ApplyAction("TriggerNow");
        TimerTriggered = true;
    }
}

public void StartTimer()
{
    IMyTimerBlock timerBlock = GridTerminalSystem.GetBlockWithName(TIMERBLOCK) as IMyTimerBlock;
    if(timerBlock != null){
        timerBlock.ApplyAction("Start");
        TimerTriggered = true;        
    }
}