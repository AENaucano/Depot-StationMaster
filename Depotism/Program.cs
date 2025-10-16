/******************************************
 * Depot system for AutoCarier
 * in conjunction with another system
 ******************************************/
﻿using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.Remoting.Messaging;
using System.Text;
using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Game.ObjectBuilders.Definitions;
using VRageMath;

namespace IngameScript
{
    public partial class Program : MyGridProgram
    {
        private const string VERSION = "0.0.1"; // Brand new

        // Tags
        public string ScriptTag = "DepotMaster"; // name of this script
        public string SpecialTag = "!Special"; // in accordance with Isy's inventory manager
        // Vars
        public string ProgramStatus = "Setup"; // Setup, running, Stopped, Reset
                // Booleans
        // public static bool EmptyCargo = false;
        public static bool Running = false;
        public static bool ResetNeeded = false; // false = normal running true = reset running
        public static bool PowerNeeded = false; // TODO bool set when < 100 -> no treshold
        public static bool Connected = false; // WARING This regulates if we recharge everything
        public static bool CargoFull = false; // true if cargo is full
        public static bool BatteriesRecharge = false; // true if PAM has set the batteries to recharge

        // Lists
        List<IMyBatteryBlock> Batteries = new List<IMyBatteryBlock>();
        List<IMyShipConnector> Cntors = new List<IMyShipConnector>();
        List<IMyCargoContainer> CargoBlocks = new List<IMyCargoContainer>();
        List<IMyRadioAntenna> Antennas = new List<IMyRadioAntenna>();
        List<string> TheMenu = new List<string>();
        List<int> Choices = new List<int>();

         //Counting time
        public DateTime StartTiming = new DateTime();
        public DateTime StopTiming = new DateTime();
        public int MaxTime = 6; // minutes

        //special stuff
        public static IMyGridTerminalSystem MyGrid;
        public static IMyProgrammableBlock ThatsMe;
        public static Program _prog;
        bool ThatsMe_Grid(IMyTerminalBlock q) => q.IsSameConstructAs(ThatsMe);

        // Setting up Classes
        public static InputClass MainInput = new InputClass();
        public static Bios BasicSystem = new Bios();
        public static AllDisplays Screens = new AllDisplays();
        // public static Items ShipItems = new Items();
        public static Cargo ShipCargo = new Cargo();
        public static Logging ScriptLog = new Logging();
        public static Communications Comms = new Communications();
        
        // stuff for debugging
        public static bool Debug = true;
        public static string DebugText = "";
        public string FastMsg = "";

        public Program()
        {
            MyGrid = GridTerminalSystem;
            ThatsMe = Me;
            _prog = this;
            Runtime.UpdateFrequency = UpdateFrequency.Once | UpdateFrequency.Update10 | UpdateFrequency.Update100;

            FastMsg = ":-> Booting\n");
            ScriptLog.clearLog();

            try
            {
                if (!Load()) FastMsg += "Loading Storage data failed\n";
                else FastMsg += "Storage data Loaded\n";
            }
            catch (Exception e) { FastMsg += "Storage not loaded: " + e.Message + "\n"); }

            Echo (FastMsg);
            ScriptLog.addLog("Program01: " + FastMsg, "Info");
            DoScan();
        }

        public void Main(string argument, UpdateType updateSource)
        {
            Echo(" ... Running " + VERSION + "\n");
            if ((updateSource & UpdateType.Update100) != 0) DoChecking();
            if ((updateSource & (UpdateType.Terminal | UpdateType.Script | UpdateType.Trigger)) != 0) GetInput(argument);
            if ((updateSource & UpdateType.Update10) != 0) DoLoop();
        }
        /*************
         * Functions *
         *************/
        public void DoScan()
        {
            ScriptLog.addLog("DoScan01: ... Scanning ", "Info");

            // LCDS
            Screens.GetAllLCDs(true); // scan all LCDs with a ScriptTag in their name

            // Connector
            List<IMyShipConnector> ConBlocks = new List<IMyShipConnector>();
            MyGrid.GetBlocksOfType(ConBlocks, ThatsMe_Grid);

            for (int cidx = 0; cidx < ConBlocks.Count(); cidx++)
            {
                Cntors.Add(ConBlocks[cidx]);
            }

            if (Cntors.Count() == 0) { ScriptLog.addLog("DoScan02: I need at least one connector", "Log"); return; }
            else { ScriptLog.addLog("DoScan02: " + Cntors.Count().ToString() + " connectors found", "Log"); }

            // Power
            Batteries.Clear();
            List<IMyBatteryBlock> BatBlocks = new List<IMyBatteryBlock>();
            MyGrid.GetBlocksOfType(BatBlocks, ThatsMe_Grid);

            for (int bidx = 0; bidx < BatBlocks.Count(); bidx++)
            {
                Batteries.Add(BatBlocks[bidx]);
            }

            if (Batteries.Count() == 0) { ScriptLog.addLog("DoScan03: No batteries ? LOL ! ", "NotFound"); }
            else { ScriptLog.addLog("DoScan04: " + Batteries.Count().ToString() + " batteries found", "Found"); CheckBatteries();}

            // CargoContainers
            CargoBlocks.Clear();
            MyGrid.GetBlocksOfType(CargoBlocks, ThatsMe_Grid);
            if (CargoBlocks.Count() == 0) { ScriptLog.addLog("DoScan04: No containers ? Have fun !", "NotFound"); }
            else { ScriptLog.addLog("DoScan04: " + CargoBlocks.Count().ToString() + " containers found", "Found"); CheckContainers(); }

            // Antennas
            Antennas.Clear();
            MyGrid.GetBlocksOfType(Antennas, ThatsMe_Grid);
            if (Antennas.Count() == 0) { ScriptLog.addLog("DoScan05: No Antenna? No communications !", "NotFound"); return;}
            else { ScriptLog.addLog("DoScan05: " + Antennas.Count().ToString() + " antenas found", "Found");}

            // Scan Done
            ScriptLog.addLog("DoScan05: Scan done", "Info");
            return;
        }
        public void DoChecking()
        {
        }
        public void DoLoop()
        {
        }
        public void GetInput(string argument)
        {
            if (UserInput == null || UserInput == "") return;

            switch (UserInput.ToLower())
            {
                case "up":
                    SetChoosenLine(-1);
                    break;
                case "down":
                    SetChoosenLine(1);
                    break;
                case "apply":
                    Applied = true;
                    break;
                case "reset": 
                    ResetNeeded = true;
                    break;
                default: return;
            }
            return;
        }   
        /***********
         * Classes *
         ***********/
        public class Logging
        {
            private string Text = "";
            private string Key = "Undef";
            private string Type = "Debug";

            private Dictionary<string, string> Emojis = new Dictionary<string, string>()
            {
                {"Info", ":->"},
                {"Good", ":-)" },
                {"Found", ":-]"},
                {"NotFound", ":-["},
                {"Error", "|-X"},
                {"Warning", ":-O"},
                {"Debug", ":-D"},
                {"WTF", ":-/"},
                {"Bad", ":-("},
                {"Undef", "°-o"},
                {"LOL", "^_^" }
            };

            private const int MAXLINES = 100;
            private List<Logging> generalLog = new List<Logging>();

            public Logging()
            {
                Text = "If you see this, something is Wrong";
                Key = "New";
                Type = "Error";
            }

            public Logging(string _key, string _txt, string _type = "Debug")
            {
                Text = _txt;
                Key = _key;
                Type = _type;
            }

            public void clearLog()
            {
                if (generalLog != null) generalLog.Clear();
            }

            public void addLog(string ThisText, string _Type = "Undef")
            {
                int OldInt = -1;
                if (String.IsNullOrEmpty(ThisText)) return;

                string newKey = "";
                string newText = "";
                string[] _splitText = ThisText.Split(':');
                if (String.IsNullOrEmpty(_splitText[1]))
                {
                    newKey = BasicSystem.GetTimeString();
                    newText = ThisText;
                }
                else
                {
                    newKey = _splitText[0];
                    newText = _splitText[1];
                }

                OldInt = FindLog(_splitText[0]); // -1 nothing found
                Logging _newLog = new Logging(newKey, newText, _Type);

                if (OldInt == -1) generalLog.Add(_newLog);
                else generalLog[OldInt] = _newLog;

                if (generalLog.Count() > MAXLINES) generalLog.RemoveAt(0);

                return;
            }

            private int FindLog(string _key)
            {
                for (int fidx = 0; fidx < generalLog.Count(); fidx++)
                {
                    if (generalLog[fidx].Key == _key) return fidx;
                }
                return -1;
            }

            /// <summary>
            /// Returns a string with a maxNumberOfLines to put on a LCD
            /// </summary>
            /// <returns></returns>
            public string printLog(bool _debugonly = false)
            {
                string newPrintlog = "";
                if (!_debugonly) newPrintlog = " === " + _prog.ScriptTag + " " + VERSION + " =Log=\n";
                else newPrintlog = " =D= Debug === " + VERSION + " =D=\n";

                if (generalLog != null || generalLog.Count != 0)
                {
                    // int startLine = generalLog.Count - maxNumberOfLines - 1;
                    // if (startLine < 0) startLine = 0;
                    //for (int i = startLine; i < generalLog.Count; i++)
                    for (int i = 0; i < generalLog.Count; i++)
                    {
                        Logging _printLog = generalLog[i];
                        if (_printLog.Type == "Debug" & !_debugonly) continue;
                        if (_printLog.Type != "Debug" & _debugonly) continue;
                        newPrintlog += " " + Emojis[_printLog.Type] + " " + _printLog.Text + "\n";
                    }
                }
                else
                {
                    newPrintlog += "... Empty";
                }

                return newPrintlog;
            }
            public List<string> ListDebug()
            {
                List<string> _Debug = new List<string>();
                if (generalLog != null || generalLog.Count() != 0)
                {
                    for (int fi = 0; fi < generalLog.Count(); fi++)
                    {
                        string _text = "";
                        Logging _printLog = generalLog[fi];
                        if (_printLog.Type != "Debug") continue;
                        _text = " " + Emojis[_printLog.Type] + " " + _printLog.Text;
                        _Debug.Add(_text);
                    }
                }
                return _Debug;
            }
        }
        public class Bios
        {
            public string EchoChars = "//"; // space gives problems
            public string GetTimeString()
            {
                DateTime now = DateTime.Now;
                return now.ToString("HH:mm:ss");
            }
            public bool DoesNameHasTag(string theTag, string Inthis)
            {
                bool Hastag = false;

                string[] _nameParts = Inthis.Split(' ');
                for (int i = 0; i < _nameParts.Length; i++)
                {
                    if (_nameParts[i].ToLower().Trim() == theTag.ToLower().Trim())
                    {
                        Hastag = true;
                    }
                }

                return Hastag;
            }
            public string GetCustomDataTag(IMyTerminalBlock thisBlock, string _thisTag)
            {
                if (thisBlock.CustomData.Trim() == "") return "";
                string _CustomData = thisBlock.CustomData.Trim();

                string[] _cdlines = _CustomData.Split('\n');
                // for each line
                for (int cdidx = 0; cdidx < _cdlines.Length; cdidx++)
                {
                    // if it does not start with // it is not mine !
                    if (_cdlines[cdidx].StartsWith(EchoChars))
                    {
                        string _cdline = _cdlines[cdidx].Replace(EchoChars, "");
                        string[] _cdwords = _cdline.Split('=');
                        if (_cdwords[0].Trim() == _thisTag.Trim()) return _cdwords[1];
                    }
                }
                // nothing found
                return "";
            }
            public double CalcPercent(double numerator, double denominator)
            {
                if (denominator == 0) return 0;
                double percentage = Math.Round(numerator / denominator * 100, 1);
                return percentage;
            }
        }
        public class AllDisplays
        {

            public const string LCDModeTag = "LCDMode";
            public const string LCDConsoleTag = "LCDConsole";

            public static List<IMyTextPanel> tmpList = new List<IMyTextPanel>();
            public static List<IMyTextPanel> AllPanels = new List<IMyTextPanel>();

            /// <summary>
            /// Gets all LCDs with the ScriptTag in their name      
            /// </summary>
            /// <param name="_rescan"></param>
            public void GetAllLCDs(bool _rescan = false)
            {
                if (_rescan)
                {
                    AllPanels.Clear();
                    List<IMyTextPanel> blocks = new List<IMyTextPanel>();
                    MyGrid.GetBlocksOfType(blocks, block => block.IsSameConstructAs(ThatsMe));

                    if (blocks != null && blocks.Count > 0)
                    {
                        for (int bidx = 0; bidx < blocks.Count; bidx++)
                        {
                            if (!BasicSystem.DoesNameHasTag(_prog.ScriptTag, blocks[bidx].CustomName)) continue;
                            AllPanels.Add(blocks[bidx]);
                            if (blocks[bidx].ContentType == ContentType.NONE) blocks[bidx].ContentType = ContentType.TEXT_AND_IMAGE;
                            if (blocks[bidx].ContentType == ContentType.TEXT_AND_IMAGE)
                            {
                                blocks[bidx].Script = "";
                                blocks[bidx].Font = "Debug";
                                blocks[bidx].FontSize = 1.4f;
                            }
                        }
                    }
                }

                // no rescan
                if (AllPanels.Count() == 0) _prog.FastMsg += "GetAllLCDs: Please make a LCD with " + _prog.ScriptTag + "\n"); 
                return;
            }
            public void displayText(string thisText, string _Tag)
            {
                tmpList.Clear();
                for (int idx = 0; idx < AllPanels.Count; idx++)
                {
                    string Pmode = BasicSystem.GetCustomDataTag(AllPanels[idx], LCDModeTag);
                    if (Pmode == _Tag) tmpList.Add(AllPanels[idx]);
                }
                if (tmpList.Count == 0) { ScriptLog.addLog("displayText: no panels with: " + _Tag, "Debug"); return; }

                for (int pidx = 0; pidx < tmpList.Count; pidx++)
                {
                    // clear panel
                    tmpList[pidx].WriteText("", false);
                    tmpList[pidx].WriteText(thisText, false);
                }
            }
            public static IMyTextSurface MedrawingSurface;
            public void printOnPB(IMyProgrammableBlock thisPB, string ScreenText, int surface = 0)
            {
                MedrawingSurface = thisPB.GetSurface(surface);
                MedrawingSurface.ContentType = ContentType.TEXT_AND_IMAGE;
                MedrawingSurface.WriteText("", false);
                MedrawingSurface.WriteText(ScreenText, false);
            }
        }
        public class Cargo
        {
            public static List<IMyCargoContainer> AllContainers = new List<IMyCargoContainer>();

            /// <summary>
            /// checks the Shps' cargo and returns a percentage of its' load in percentage.
            /// </summary>
            /// <returns>float(percent)</returns>
            public float CheckShipCargo()
            {
                MyFixedPoint TotalMaxVol = 0;
                MyFixedPoint TotalCurrentVol = 0;
                // _prog.AllShipItems = CreateList(); // reset the listing
                foreach (IMyCargoContainer _ShipCargo in _prog.CargoBlocks)
                {
                    TotalMaxVol += _ShipCargo.GetInventory(0).MaxVolume;
                    TotalCurrentVol += _ShipCargo.GetInventory(0).CurrentVolume;
                    // TODO We will rename each Cargo with the fill level
                    int CargoPercent = Convert.ToInt32(BasicSystem.CalcPercent((float)_ShipCargo.GetInventory(0).CurrentVolume, (float)_ShipCargo.GetInventory(0).MaxVolume));

                }
                if (TotalCurrentVol == 0) EmptyCargo = true;
                if (TotalCurrentVol >= TotalMaxVol) CargoFull = true;

                return (float)BasicSystem.CalcPercent((float)TotalCurrentVol, (float)TotalMaxVol);
            }
        }
        public class InputClass
        {
            /******************
             * Antenna system *
            ******************/
        
            public bool SendMessage(string SendMessage="AntennaTest", string Header = SendMessageHeader )
            {
                hasSend = false;

                IMyRadioAntenna Antenna = _prog.Antennas[0] as IMyRadioAntenna; // TODO is there a way to choose what antenna to use ?
                hasSend = Antenna.TransmitMessage(Header + "=" + SendMessage);
                if(!hasSend) ScriptLog.addLog("SendMessage02: Sending message failed", "Error");
                else ScriptLog.addLog("SendMessage02: Message sent: " + SendMessage, "Info");
        
                return hasSend;
            }
 
            /// <summary>
            /// Gets Up, Down, Apply from the PB Run (command)
            /// </summary>
            /// <param name="UserInput"></param>

            public void GetInput(string UserInput)
            {
                _prog.Applied = false;
                if (UserInput == null || UserInput == "") return;

                switch (UserInput.ToLower())
                {
                    case "up":
                        _prog.SetChoosenLine(-1);
                        break;
                    case "down":
                        _prog.SetChoosenLine();
                        break;
                    case "apply":
                        _prog.Applied = true;
                        break;
                    case "reset": // WARNING will only run by next itteration
                        // BasicSystem.Setup = false;
                        // BasicSystem.SaveMe();
                        ResetNeeded = true;
                        break;
                    default: return;
                }

                return;
            }

        }
    }
    public class Communications
    {
        string Header;
        string Message;
        string Source;

        List<IMyBroadcastListener> listeners = new List<IMyBroadcastListener>();
        List<Communications> broadcasts = new List<Communications>();

        public Communications(string _Header, string _Message, string _Source)
        {
            Header = _Header;
            Message = _Message;
            Source = _Source;
        }

        public bool SetupCom()
        {
    	    if _prog.Antennas.Count() == 0) return false;
            // Create a list for broadcast listeners.
    	    IGC.GetBroadcastListeners(listeners);
            if (listeners.Count == 0) { ScripLog.addLog("SetupCom01: No listeners found", "Error"); return false;}
            return true;
        }    

        public bool ReceiveMessage()
        {    
    	    bool Received = false;
            if(listeners.Count == 0) return Received;
            if(listeners[0].HasPendingMessage)
	        {
    		    MyIGCMessage message = new MyIGCMessage;
        		message = listeners[0].AcceptMessage();
		        string messagetext = message.Data.ToString();
		        string messagetag = message.Tag;
		        long sender = message.Source;

		        // Do something with the information!
		        // Echo("Message received with tag" + messagetag + "\n");
		        // Echo("from address " + sender.ToString() + ": \n");
		        // Echo(messagetext);
                communications newMessage = new communications(messagetag, messagetext, sender.ToString());
                broadcasts.Add(newMessage);
                Received = true;
            }
            return Received
		}

        public void SendMessage(string SendMessage="AntennaTest", string Header = "AntennaTest" )
        {
            IGC.SendBroadcastMessage(Header, SendMessage, TransmissionDistance.TransmissionDistanceMax);
        }
	}
}