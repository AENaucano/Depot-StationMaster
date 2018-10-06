/********************************************
	Ore Transport Ship
*********************************************/

// Where is the newest version ?


public Program(){
    Runtime.UpdateFrequency = UpdateFrequency.Update100;
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
        ProgramStatus ?? "Wait",
        Position ?? "@Base");
}

const string VERSION = "2.0";

const string LCDNAME = "DroneLCD";
const string FREIGHTCONTAINER = "MContainer Drone Remorque";

const string DRONEBATTERY = "Drone Battery";
// Change this to something else if you get interferences from otyher SAMs
const string DRONESAM = "SAMDRONE";

const string DRONECONNECTOR = "Drone Connector " + DRONESAM;

bool SAMRunOK = false;
int RunNumber = 0;

string ProgramStatus = "Euh";

string Position = "Error";

// Typical nameholders
// Later this could be a list of Containers now there is only one
public IMyCargoContainer OreContainer;
// idem :)
public IMyShipConnector DroneConnector;
public IMyProgrammableBlock DronePB;

// fancy stuff
public static List<char> SLIDER_ROTATOR = new List<char>(new char[] { '-', '\\', '|', '/' }); 
public DisplaySlider rotator = new DisplaySlider(Program.SLIDER_ROTATOR); 

string Message = " AAh! \n";

// =============================================================== Main ====================================================================
public void Main(string argument, UpdateType updateSource) {
 
    Message = rotator.GetString() + " Drone Control " + VERSION + " ... \n";
    // Echo (Message);

    // We need a connector & its inventory
    int ConnectorStatus = CheckConnector();
    switch (ConnectorStatus) {
        case -1:
            Message += " > Connector not found\n or not usable\n";
            break;
        case 0:
            Message += " > Connector\n can not connect\n to anything\n";
            break;
        case 1:
            // SAM Missed this or we are Manual
            Message += " °-] Connector NOT connected\n > but could connect\n";
            // leave it to SAM ;) ConnectConnector();
            break;
        case 2:
            Message += " > Connector connected\n";
            break;
        default:
            Message += " > this is akward\n check Connector\n";
            break;
    }

    // We need container ( inventory and contenance)
    string ContainerStatus = CheckContainer();
    switch (ContainerStatus) {
        case "Full":
            Message += " > " + FREIGHTCONTAINER + "\n full\n";
            break;
        case "Empty":
            Message += " > " + FREIGHTCONTAINER + "\n empty\n";
            break;
        case "Error":
        default:
            Message += " |-] " + FREIGHTCONTAINER + "\n error\n";
            break;    
    }
     
    // We need power -> Battery -> reactor needs Uranium Ingot - Solar panels don’t
    float PowerStatus = Checkbatteries();
    // E=P*t => E/p = t
    float TotalSeconds = ( PowerStatus * 3600)/ CurrentOut;
    int Secondstime = Convert.ToInt32(Math.Floor(TotalSeconds));
    TimeSpan BatterieTime = new TimeSpan(0,0,0,Secondstime);

    string BatterieTimeFormated = string.Format("{0:D2} {1:D2}:{2:D2}:{3:D2}",
                BatterieTime.Days, 
                BatterieTime.Hours, 
                BatterieTime.Minutes, 
                BatterieTime.Seconds
                );
    Message += " > Energy until:\n" + BatterieTimeFormated + " \n";
    // how do we know there is enough power ? -> we don't ... until we fly !
    // We need enough Thrusters -> at least 2 large ones if you have medium container

    // We need a PC with SAM -> with connection ?
    if (!CheckSAM()) {
        Message += " |-] No SAM? \n";
        ShowText (Message, LCDNAME, true);
        return;        
    }

    //DEBUG
    // Message += "Position: " + Position + "\n";

    if((ProgramStatus != "Arrived@Mine")&&(ProgramStatus != "Arrived@Base"))
    {
        // @ the Base
        if(Position == "@Base")
        {
	        // Lock Connector [SAM] -> Check anyway ;)
	        // Check inventory Container
		    // > 0 -> wait -> Ore Master Process
            if(ContainerStatus != "Empty")
            {
                ProgramStatus = "Wait";
                Save();
            }
            else
            {
		        // == 0 -> Fetch new ore
                ProgramStatus = "GotoMine";
                Save();
            }
        // -> Waiting
		    // Check fuel -> Amount in stock ? Conveyor ? 
	    // Fetch new ore
		    // Command SAM to goto the mine connector
		    // & wait for SAM signaling we arrived @ the Mine

        }

        // @ the Mine
        if(Position == "@Mine")
        {
	    // Lock Connector [SAM]
	    // if Mine Containers > 0
            if(ContainerStatus != "Full")
            {
                ProgramStatus = "Wait";
                Save();
            }
            else
            {
		        ProgramStatus = "GotoBase";
                Save();
                // transfer to Ship Container(s) 
		        // if full
			        // stop transfer
			        // Set SAM to base
			        // Start SAM
            }
        }
    } 

   // DEBUG
   Message += "ProgramStatus: " + ProgramStatus + "\n";

    switch (ProgramStatus)
    {
        case "Wait":
            break;
        case "GotoMine":
            GotoMine();
            ProgramStatus = "Arrived@Mine";
            Save();
            Message += " > to Mine\n";
            break;
        case "Arrived@Mine":
            Message += "ConnectorStatus: " + ConnectorStatus.ToString() + "\n";
            if(ConnectorStatus == 2)
            {
                Position = "@Mine";
                ProgramStatus = "Wait";
                Message += " Arrived @ Mine\n";
                SAMRunOK = false;
                Save();                 
            }
            else
            {
                Message += " > Flying °°°\n";
            }            
            break;
        case "GotoBase":
            GotoBase();
            ProgramStatus = "Arrived@Base";
            Message += " > Going Home\n";
            Save();
            break;
        case "Arrived@Base":
            if(ConnectorStatus == 2)
            {
                Position = "@Base";
                ProgramStatus = "Wait";
                Message += " > Arrived @ Base\n";
                SAMRunOK = false;
                Save();                 
            }
            else
            {
                Message += " > Flying °°°\n";
            }            
            break;                
        
    }

    ShowText (Message, LCDNAME, true);
}

/**************
    Container
 **************/
public string CheckContainer()
{
    string OreContainerStatus = "Error";
    IMyCargoContainer OreContainer = GridTerminalSystem.GetBlockWithName(FREIGHTCONTAINER) as IMyCargoContainer;
    if(OreContainer == null)
    {
        Message += "WTF? \n";
        return OreContainerStatus;
    }

    IMyInventory ThisStock = OreContainer.GetInventory(0);

    if(ThisStock.IsFull){OreContainerStatus = "Full";}
    if(ThisStock.CurrentVolume == 0){OreContainerStatus = "Empty";}

    return OreContainerStatus;
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
    IMyShipConnector DroneConnector = GridTerminalSystem.GetBlockWithName(DRONECONNECTOR) as IMyShipConnector;
   
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
    IMyShipConnector DroneConnector = GridTerminalSystem.GetBlockWithName(DRONECONNECTOR) as IMyShipConnector;
    if(DroneConnector != null)
    {
        DroneConnector.Connect();
    }
    else
    {
        Message += " |-[ could not connect " + DRONECONNECTOR + " \n";
    }
}

/*****************
    batteries
*******************/
float MaxEnergy = 0.0f;
float StoredEnergy = 0.0f;
float CurrentIn = 0.0f;
float CurrentOut = 0.0f;

public float Checkbatteries()
{
  StoredEnergy  = 0.0f;
  MaxEnergy = 0.0f;  
  CurrentIn =  0.0f;  
  CurrentOut = 0.0f;  
  
  IMyBatteryBlock Battery = GridTerminalSystem.GetBlockWithName(DRONEBATTERY) as IMyBatteryBlock;
  if(Battery != null)
  {
    StoredEnergy = Battery.CurrentStoredPower;
    MaxEnergy = Battery.MaxStoredPower;  
    CurrentIn =  Battery.CurrentInput;  
    CurrentOut = Battery.CurrentOutput;
   }
   else
   {
       Echo("huh? No battery ? \n");
   }

  return StoredEnergy;
}

/*********
    SAM
**********/

public bool CheckSAM()
{
    List<IMyTerminalBlock> PB_blocks = new List<IMyTerminalBlock>();
    GridTerminalSystem.SearchBlocksOfName(DRONESAM, PB_blocks, b => b is IMyProgrammableBlock);

    if (PB_blocks == null) return false;
   
    return true;   
}

public void GotoMine()
{

   IMyProgrammableBlock PCSam = GridTerminalSystem.GetBlockWithName(DRONESAM) as IMyProgrammableBlock;

   if(!SAMRunOK)
    {
        RunNumber++;
        Message += "Running Sam ..." + RunNumber + "\n";
        PCSam.TryRun("DOCK NEXT");
        PCSam.TryRun("NAV START");    
        SAMRunOK = true;
    }
}

public void GotoBase()
{

   IMyProgrammableBlock PCSam = GridTerminalSystem.GetBlockWithName(DRONESAM) as IMyProgrammableBlock;

   if(!SAMRunOK)
    {
        RunNumber++;
        Message += "Running Sam ..." + RunNumber + "\n";
        PCSam.TryRun("DOCK PREV");
        PCSam.TryRun("NAV START");    
        SAMRunOK = true;
    }
}

//-----------------------------------------------------------------------------

public float countItem(IMyInventory inv, string itemType, string itemSubType)
{
    var items = inv.GetItems();
    float total = 0.0f;
    for(int i = 0; i < items.Count; i++)
    {
        // tekst += " > Items: " + items[i].Content.SubtypeId.ToString() + "\n";
        if((items[i].Content.TypeId.ToString().EndsWith(itemType) && (items[i].Content.SubtypeId.ToString() == itemSubType)))
        {
            total += (float)items[i].Amount;
        }
    }
    return total;
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

public class DisplaySlider { 
    public List<char> displayList; 
    public DisplaySlider(List<char> l) { 
        this.displayList = new List<char>(l); 
    } 
    public string GetString() { 
        this.displayList.Move(this.displayList.Count() - 1, 0); 
        return this.displayList.First().ToString(); 
    } 
} 