/*
// DrillSequencer
// Pistons are PistonP
// Drills are DrillD
// One Rotor RotorR1 ( advanced!)
// Status
// Manual : you have control ;)
// Reset : Init sequence
// Running : Normally drilling
// Stopping : such as ending the drilling
// Adapted for Antenna system
/// -> still buggy
// It should say when it is stuck ie. the rotor does not move
// 
/// Depotmasters need to know: What mine and what ore
    -> we will use CustomData of the PB to designate a Nato Code
    ->  Station=NATOcode ex 
    -> there should be a way that DM has control on what code to use
/// -> this scipt has to send what kind of ore it has ? -> how ?
*/

string ProgramStatus = "Manual";
float Next_Position;

public Program()    
{    
    Runtime.UpdateFrequency = UpdateFrequency.Update100;

    string[] storedData = Storage.Split(';');
    if (storedData.Length >= 1)
    {
        ProgramStatus = storedData[0];
    }

    if (storedData.Length >= 2)
    {
        float.TryParse(storedData[1], out Next_Position);
    }
}    

public void Save()
{
    Storage = string.Join(";",
        ProgramStatus ?? "Manual", 
        Next_Position
    );
}    
// This is persistent !

/********************************************** 
 Change this to the desired distance, if any   
***********************************************/
float Pistons_Wanted_Length = 00.0f;   

/**********************************************/

const string version = "01.06";
float R_Angle;
float Old_R_Angle;  

bool Pistons_Done;
const float Piston_Max_Lenght = 10.0f;   
   
const string LCD_Pattern = "Drilling";   
const string ROTORNAME = "RotorR1";   
const string Drill_Pattern = "DrillD";   
const string Piston_Pattern = "PistonP";

// new Calling the drone system
const string AntennaName = "Antenna MineShip"; // Change this to the correct name
const string AntennaMaster = "AntennaMaster";

const string MINECONTAINERPATTERN = "Mine";
const string SendMessageHeader = ""; // NatoCode in string
int SendTimer = 0;

// connector
List<IMyTerminalBlock> DroneConnectors = new List<IMyTerminalBlock>();
const string DRONECONNECTOR = "Connector Mine";
public IMyShipConnector DroneConnector;

// Rotor  
const float Rotor_ResetVelocity = 1f;  

// Pistons
float Pistons_Current_Length = 0f;   
const float Piston_RetractVelocity = 0.5f;

bool Pistons_AreMoving;
bool Rotor_FinishedTurn;
bool Rotor_FinishingTurn;
bool HoustonWeHaveAProblem = false;
bool NoStone = true;
bool BaseAck = false; // base has Ackknowledged, don't send anything
bool sendCall = false;

List<IMyTerminalBlock> MineOreContainers = new List<IMyTerminalBlock>();
List<IMyTerminalBlock> MineConnectors = new List<IMyTerminalBlock>();

// Containers
List<string> OreTypes = new List<string>(); // duplicates possible

// From SAM
static List<string> NATO_CODES = new List<string> { "Alfa", "Bravo", "Charlie", "Delta", "Echo", "Foxtrot", "Golf", "Hotel", "India", "Juliett", "Kilo", "Lima", "Mike", "November", "Oscar", "Papa", "Quebec", "Romeo", "Sierra", "Tango", "Uniform", "Victor", "Whiskey", "Xray", "Yankee", "Zulu" }; 
// Following SAM -> The string version = MessageHeader
int NATO_Index = 1; // 0-based 1=Bravo, alfa reserved for Base

// OreControl ... I know scrap is not a ore ... but is important for DM
static List<String> SubOreTypeList = new List<string> {  "Iron", "Nickel","Silicon", "Cobalt", "Magnesium", "Uranium",  "Silver", "Gold", "Platinum", "Scrap", "Stone" };
List<float> SubOreTypeAmounts = new List<float> { 0f,0f,0f,0f,0f,0f,0f,0f,0f,0f,0f };

// Text heading    
string Message;   
  
// anything in Main is subject to change

/*****************
    Main loop
 ****************/

public void Main(string argument, UpdateType updateSource)    
{    
    
    Message = " Automated Drill Sequence - Main loop\n";

    //DEBUG
    Echo("Argument: " + argument + "\n");


    CheckCustomdata();

    if (argument.Length > 0)
    {
        // If it gets its own Header back it is an Acknowledgement from the base
        if (argument.Contains(SendMessageHeader))
        {
            var ReceivedMessage = argument.Split(':','>','/','=');
            if(ReceivedMessage[1].Contains("Ack")) { BaseAck = true; }
        }
    }

    if (ProgramStatus == "Manual")
    {
        switch (argument.ToLower())
        {
            case "init":
                ProgramStatus = "Reset";
                break;
            case "reset":
                ProgramStatus = "Reset";
                break;
            case "run":
                ProgramStatus = "Running";
                break;
            case "go":
                ProgramStatus = "Runnning";
                break;
           case "manual":
                ProgramStatus = "Manual";
                break;                
            default:
                Message += " Give me an argument : Init (Reset) or Run (Go) ... \n";
                break;
        }
    }
    else
    {
        switch (argument.ToLower())
        {
            case "stop":
                ProgramStatus = "Stopping";
                break;
            case "halt":
                ProgramStatus = "Stopping";
                break;
            case "manual":
                ProgramStatus = "Manual";
                break;
            default:
                Message += " ... Running ... \n by \"stop\" the reset sequence is run ... \n";
                break;
        }
        
    }

    if(Pistons_Wanted_Length <= 0f) 
    {
        //DEBUG
        // Echo("BaseAck: " + BaseAck + "\n");
        if(!sendCall) { SendStatus("Not_Configured"); }
        // Echo("sendCall: " + sendCall + "\n");
        Message += " --> Check Config\n";
        ShowText(Message, LCD_Pattern, true);           
        return;
    }

    // when it works comment this line out
    // Echo("Argument passing into ProgramStatus: " + ProgramStatus + "\n");
    
    switch (ProgramStatus)
    {
        case "Reset":
            Message += " Init Sequence ...\n";
            // if this is false we do not have drills
            // or not with the right name anyway
            if (!InitDrills())
            {
                return;
            }
            
            if(!ResetRotor())
            {
                Message += " °-0 Check Rotor \n";
            }
                        
            if(InitPistons())
            {
                Message += " done\n";
            }

            StopDrills();

            Next_Position = 0f;
            ProgramStatus = "Manual";

            // sync storage
            Save();
            SendStatus("Resetting");
            break;

        case "Stopping":
            Message += "Holding the horses\n";
            SendStatus("Stopping");            
            // Get the pistons retract first now
            if (InitPistons())
            {
                // one more turn of the rotor
                if (ResetRotor())
                {
                    // stop the drills & back to manual control
                    StopDrills();
                    ProgramStatus = "Manual";
                }
            }

            break;
        case "Running":
            Message += " ... turning the wheels ...\n";
            
            InitDrills();           
            // if true Rotor has turned fullcircle
            if (Turn_Rotor(Pistons_AreMoving))
            {
                if(HoustonWeHaveAProblem)
                {
                    break;
                }
                
                // if true Next position is reached
                if(Run_Piston())
                {
                    // Pistons on the move
                    Pistons_AreMoving = false;
                    
                    // Start the rotor again
                    Run_Rotor();

                    // if true reached final wanted position
                    if (Pistons_Done)
                    {
                        ProgramStatus = "Stopping";
                        SendStatus("Finished");                        
                    }
                }
                else
                {
                    Pistons_AreMoving = true;  
                }
            }

            // Check container(s) for Ore
            // Message += "Amount Mine " + CheckMine() + "/" + MinimAmount + "\n";
            // Mine Sends always ore Amount so this can be empty  
            SendStatus(Pistons_Current_Length.ToString("0.00") + "|" + Pistons_Wanted_Length.ToString("0.00") + "m");           
            // SendStatus(Pistons_Current_Length.ToString("0.00") + "|" + Pistons_Wanted_Length + "m");            
            break;
        case "Manual":
            Message += "You are in control ;) \n";
            SendStatus("Manual");            
            Echo("Mannually controlled ;)\n");
            break;
        default:
            Echo("Something is amiss with ProgramStatus: " + ProgramStatus + "\n");
            break;
    }

    Pistons_Current_Length = GetCurrentLength();
    Message += " :-) Current position: " + Pistons_Current_Length.ToString("0.00") + "m of  " +  Pistons_Wanted_Length  + "m\n";
    Message += " :-} Next position: " + Next_Position + "\n";

    if (!sendCall && !BaseAck)
    {
        Message += " --> Failed to call the Base\n";
    }
    else
    {
        Message += " --> Message Send\n --> Acknowledged: " + BaseAck + "\n";
    }

    // Fall thru to next Rotor run  
    ShowText(Message, LCD_Pattern, true);     
}

/****************** 
    subroutines  
*******************/

public void CheckCustomdata(){
    string MyData = Me.CustomData;

    if (MyData = "") { 
        Me.CustomData = "Station=Fill this in\n";
        return; 
    }
    
    string[] lines = MyData.Split('=', '\n');
    foreach(var line in lines){
        if(line[0].Contains("Station")){
            if(NATO_CODES.Contains(line[1])){
                MessageHeader = Line[1];
            } else {
                Message += "Check Customdata\n";
            }
        }
    }

    return;
}

public bool Run_Piston()
{
    float Piston_DeltaDistance = 1.0f;  

    int Piston_number = 0;
    string PistonPName = "";   

    float Piston_DrillVelocity = 0.1f; // +n extend -n retract
    float Piston_Next_Position = 0.0f; 

    /*******************************   
        Checking status of Pistons  
    *********************************/   
    List<IMyTerminalBlock> MyPistons = new List<IMyTerminalBlock>();    
    GridTerminalSystem.SearchBlocksOfName(Piston_Pattern, MyPistons);   
   
    // if there are no pistons don't bother   
    if (MyPistons == null)  
    {   
        Message += " There is no " + Piston_Pattern + " Piston on the grid -> done\n";
        return true;  
    }   
    else   
    {   
        // We DO have Pistons   
        // decide which Piston -> max-length 10m / piston  
        Piston_number = (int) Math.Truncate(Pistons_Current_Length / Piston_Max_Lenght) + 1;  
      
        PistonPName = Piston_Pattern + Piston_number.ToString();  
        // this does not work anymore
        // IMyPistonBase PistonP = GridTerminalSystem.GetBlockWithName(PistonPName) as IMyPistonBase;  
        // this does I hope
        IMyPistonBase PistonP = MyPistons[Piston_number - 1] as IMyPistonBase;

        
        // Get the Total length of all pistons
        Pistons_Current_Length=GetCurrentLength();
           
        if (Pistons_Current_Length != Pistons_Wanted_Length)   
        {   
            if(Next_Position == 0f)
            {
                Next_Position += Piston_DeltaDistance;
            }
                
            // It is possible that Current length > Next Position
            if(Pistons_Current_Length > Next_Position)
            {
                Next_Position = (float) Math.Truncate(Pistons_Current_Length) + Piston_DeltaDistance;
            }

            // If pistons reached Next_position then return true
            if (Pistons_Current_Length == Next_Position)
            {

                if(PistonP != null)
                {
                    PistonP.Velocity = 0.0f;   
                }
                else
                {
                    Message += " Can not reach " + Pistons_Wanted_Length + "\n not enough pistons !\n";
                }

                Next_Position = (float) Math.Round(Pistons_Current_Length) + Piston_DeltaDistance;

                Piston_Next_Position = Next_Position - ((Piston_number - 1) * Piston_Max_Lenght);

                Message += " :-) next Piston " + Piston_number + " position: " + Piston_Next_Position.ToString("0.00") + "m of  " +  Pistons_Wanted_Length  + "m\n";
            
                // Pistons arrived at next position
                return true;          

            }
            
            // We are still moving
            Piston_Next_Position = Next_Position - ((Piston_number - 1) * Piston_Max_Lenght);

            if (PistonP.MaxLimit != Piston_Next_Position)
            {
                PistonP.MaxLimit = Piston_Next_Position;
            }    
                
            PistonP.Velocity = Piston_DrillVelocity * -1;    
            PistonP.ApplyAction("Reverse");    

            Message += " :-) next Piston " + Piston_number + " position: " + Piston_Next_Position.ToString("0.00") + "m of  " +  Pistons_Wanted_Length  + "m\n";
                
            return false;
            
        }
        else
        {
            Message += " :-D reach final destination:  " +  Pistons_Wanted_Length  + "m\n";    
            Pistons_Done = true;
            return true;
        }
    }
} 

/***************   
    Piston   
****************/   

public bool ReachedPosition(float MyNumber)   
{   
    bool result = false;   
    float TruncatedPart = (float) Math.Truncate(MyNumber);   
    if ((MyNumber - TruncatedPart) == 0 )   
    {   
        result = true;   
    }   
    return result;   
   
}   
 
public bool InitPistons()
{
    List<IMyTerminalBlock> MyPistons = new List<IMyTerminalBlock>();    
    GridTerminalSystem.SearchBlocksOfName(Piston_Pattern, MyPistons);  

    if (MyPistons == null)
    {
        Message += "No piston " + Piston_Pattern +  " found\n";
        return true;
    }

    Message += " Returning Pistons to startposition \n";    

    for (int i = 0; i < MyPistons.Count; i++)   
    {   
        if(MyPistons[i] != null)
        {
            string ListPistonName = MyPistons[i].CustomName;   
            IMyPistonBase PistonAll = GridTerminalSystem.GetBlockWithName(ListPistonName) as IMyPistonBase;

            PistonAll.MaxLimit = 0;
            PistonAll.Velocity = Piston_RetractVelocity;    
            PistonAll.ApplyAction("Reverse");
        }
          
    }
    
    return true; 
}

public float GetCurrentLength() 
{ 
    List<IMyTerminalBlock> MyPistons = new List<IMyTerminalBlock>();    
    GridTerminalSystem.SearchBlocksOfName(Piston_Pattern, MyPistons);  
    
    float CLength = 0f; 
 
    if (MyPistons != null)
    {  
        // We DO have Pistons   
        // Get the Total length of all pistons   
        for (int i = 0; i < MyPistons.Count; i++)   
        {   
            if (MyPistons[i] != null)
            {
                string ListPistonName = MyPistons[i].CustomName;   
                IMyPistonBase Piston = GridTerminalSystem.GetBlockWithName(ListPistonName) as IMyPistonBase;    
                if(Piston != null)   
                {   
                    CLength += Piston.CurrentPosition;   
                }
            }   
        } 

    } 
    return CLength;
} 

/**********   
   Rotor    
**********/

public bool ResetRotor()
{
    IMyMotorStator RotorR1 = GridTerminalSystem.GetBlockWithName(ROTORNAME) as IMyMotorStator;    

    bool Rotor_Result = false;

    if (RotorR1 == null)
    {
        return true;
    }
    else if (!RotorR1.IsAttached) 
    {
        Message += ROTORNAME + " detached \n";
        HoustonWeHaveAProblem = true;
        ProgramStatus = "Manual";    
        return false;
    }     

    else
    {
    
        Rotor_FinishedTurn = false; 
        float MyResult = (float) Convert.ToDouble((RotorR1.DetailedInfo.Remove(0,25)).TrimEnd('°')); 

        // what's with the 5° ?
       //  if (MyResult != 5f)
        if (MyResult > 0f)
        {
           
            RotorR1.SetValue("UpperLimit", 361f);
            RotorR1.SetValue("LowerLimit", 0f);   

            RotorR1.SetValue("Velocity", 0.2f );
            RotorR1.ApplyAction("Reverse");                

            Message += " Returning Rotor to 0° startposition " + MyResult + "° \n";    
        }
        else
        {
            // He does not want the Rotor_Stop ????

            RotorR1.SetValue("Velocity", 0f );

            RotorR1.SetValue("UpperLimit", 361f);
            RotorR1.SetValue("LowerLimit", 0f);

            Message += " Rotor @ startposition " + MyResult + "° \n";       

            Rotor_Result = true; 
        }
    }

    return Rotor_Result;
}

// WTF ?

public bool Run_Rotor()
{
    IMyMotorStator RotorRun = null;
    RotorRun = GridTerminalSystem.GetBlockWithName(ROTORNAME) as IMyMotorStator;         

    if (RotorRun == null)      
    {
        Message += " No " + ROTORNAME + " found \n";    
        return false;
    }
    else if (!RotorRun.IsAttached) 
    {
        Message += ROTORNAME + " detached \n";
        HoustonWeHaveAProblem = true;
        ProgramStatus = "Manual";    
        return false;
    }     
    
    RotorRun.SetValue("UpperLimit", 361f);
    RotorRun.SetValue("LowerLimit", 0f);   
    RotorRun.SetValue("Velocity", -0.1f ); 

    RotorRun.ApplyAction("Reverse");

    Rotor_FinishedTurn = false; 

    return true;
}

public bool Turn_Rotor(bool NotNow=false)   
{   
    if (NotNow)
    {
        return true;
    }

    IMyMotorStator RotorR1 = null;
    RotorR1 = GridTerminalSystem.GetBlockWithName(ROTORNAME) as IMyMotorStator;         
       
    if (RotorR1 == null)      
    {     
        Message += " No " + ROTORNAME + " found \n";    
        return true;   
    }
    else if (!RotorR1.IsAttached) 
    {
        Message += ROTORNAME + " detached \n";
        HoustonWeHaveAProblem = true;
        ProgramStatus = "Manual";    
        return true;
    }     

    // rotor is turning
    float EndAngle = 359.0f; // 360==0 !  

    R_Angle = (float) Convert.ToDouble((RotorR1.DetailedInfo.Remove(0,25)).TrimEnd('°'));

    if((R_Angle != Old_R_Angle)&&(Old_R_Angle != 0f))
    {
        Old_R_Angle = R_Angle;
    }
    else
    {
        SendStatus("Stuck");
        Message += " |-[ I think I am Stuck\n";
    }

    if(R_Angle >= EndAngle) 
    {
        Message += " --> " + ROTORNAME + " finishing ... \n"; 
        Rotor_FinishingTurn = true;
    }

    if((R_Angle < 10.0f)&&(Rotor_FinishingTurn))
    {
        Rotor_FinishingTurn = false;
        Rotor_FinishedTurn = true;
    }

    if (!Rotor_FinishedTurn)
    {
        RotorR1.SetValue("Velocity", 0.1f );   
        Message += " --> " + ROTORNAME + " @ Angle: " +  R_Angle + " -> " + EndAngle + "\n";
    }
    else
    {
        Message += " --> " + ROTORNAME + " finished its turn\n";
        RotorR1.SetValue("Velocity", 0f);   
        return true;  
    }   

    return false;
}   

/**********************  
    Drills  
***********************/  

public bool InitDrills()
{   
    List<IMyTerminalBlock> MyDrills = new List<IMyTerminalBlock>();      
    GridTerminalSystem.SearchBlocksOfName(Drill_Pattern, MyDrills);   
   

    if(MyDrills == null)   
    {   
        Message += " There are no " + Drill_Pattern + " drills on the grid\n What am I suppose to do ?\n";
        return false;       
    }   
    else  
    {  
        // 0-based 1 drill means only MyDrills[0].CustomName;
        for (int i = 0; i < MyDrills.Count; i++)  
        {  
            string DrillName = MyDrills[i].CustomName;  
            FindDrill(DrillName, "OnOff_On");
        }   
    }  

    return true;

}

public void StopDrills()
{
    List<IMyTerminalBlock> MyDrills = new List<IMyTerminalBlock>();      
    GridTerminalSystem.SearchBlocksOfName(Drill_Pattern, MyDrills);   
   
    for (int i = 0; i < MyDrills.Count; i++)  
    {  
        string DrillName = MyDrills[i].CustomName;  
        FindDrill(DrillName, "OnOff_Off");
    }   

    return;

}

public bool FindDrill(string DrillName, string command)    
{    
    IMyShipDrill DrillD = GridTerminalSystem.GetBlockWithName(DrillName) as IMyShipDrill;    
    
    if(DrillD != null)    
    {    
        // string DrillTekst = "-> Drill @ " + DrillName + " " + command + "\n";   
        DrillD.ApplyAction(command);    
  
        return true;    
    }    
    
    return false;    
}    

/****************   
    LCD   
******************/   
    
public void ShowText(string Tekst, string LCDName = "LCD ComponentControl",  bool RepeatEcho = false)     
{     
    List<IMyTerminalBlock> MyLCDs = new List<IMyTerminalBlock>();     
    GridTerminalSystem.SearchBlocksOfName(LCDName, MyLCDs);   
   
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

/****************
    containers
 *****************/

// Check the inventory of the Mine
// F**** ! Connectors have inventory too ! they are counted as well
// and ... stone ?
public float CheckMine() {
    float AmountOres = 0f;
    SubOreTypeAmounts.Clear();
    OreTypes.Clear();

    GridTerminalSystem.SearchBlocksOfName(MINECONTAINERPATTERN, MineOreContainers, c => c.HasInventory);
    if (MineOreContainers == null ) return 0;

    for (int i = 0; i < MineOreContainers.Count; i++) {
        var MineContainer = MineOreContainers[i];
     
        IMyInventory ThisStock = MineContainer.GetInventory(0);    
        findOre(ThisStock); // -> Add subOreType to OreTypes
        
        foreach (var OreType in OreTypes) {
            // its up to DM if it wants stone or not
            // if ((OreType != "Stone") && (NoStone))
            // {
                AmountOres += countItem(ThisStock, OreType);
            // }
        
            // sort out by type -> SubOreType
            int oreIndex = SubOreTypeList.IndexOf(OreType);
            // it can not be 0, the list is not empty
            if(oreIndex != 0){
                // The amounts are in sync with the names
                float TotalAmountOf = SubOreTypeAmounts[oreIndex];
                // add the amount of ore to the total
                TotalAmountOf += AmountOres;
                // put it back in the correct index
                SubOreTypeAmounts.Insert(oreIndex, TotalAmountOf)
            }
        }
    }

    return AmountOres;
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

// this can be used to check what ore we have if
// we check the subtype
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

/***************
 antenna system
***************/
List<IMyTerminalBlock> Antennas = new List<IMyTerminalBlock>();
public void SendStatus(string SendMessage="Test")
{
    // SendTimer++;
    // if (SendTimer < 20) return;
    // SendTimer = 0;

    sendCall = false;
    GridTerminalSystem.SearchBlocksOfName(AntennaName, Antennas, block => block is IMyRadioAntenna);

    if ((Antennas == null)||(Antennas.Count == 0))
    {
        // Echo("Antenna " + AntennaName +" Not found\n");
        Message += " There are no " + AntennaName + " antennas on the grid\n No Mesages will be send\n";
        return;
    }

    // Echo("Antenna " + AntennaName +" OK! \n");
    IMyRadioAntenna Antenna = Antennas[0] as IMyRadioAntenna;

    // Mine sends ALWAYS if there is ore
    // Now DepotMaster asks for the name of the ore -> it is in Oretypes
    // there could be more then one type and stone becomes a valid ore ... or not ?
    CheckMine();

        string MineMessage = "Ore>" + CheckMine().ToString() + "/" + SendMessage;

    // sendCall = Antenna.TransmitMessage(SendMessageHeader + "=" + MineMessage, MyTransmitTarget.Everyone);

    sendCall = Antenna.TransmitMessage(SendMessageHeader + "=" + MineMessage);

    if(sendCall) 
    {
        Echo("OK send: " + SendMessageHeader + "=" + MineMessage + "\n");
        return;
    }
    else
    {
        Echo("Send failed\n");
    }

    return;
}