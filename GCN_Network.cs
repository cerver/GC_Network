using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.ComponentModel;
using System.Timers;
using System.Windows.Threading;
using System.Net;
using System.Net.Sockets;

using Bentley.GenerativeComponents;
using Bentley.GenerativeComponents.Features;
using Bentley.GenerativeComponents.Features.Specific;
using Bentley.GenerativeComponents.GCScript;
using Bentley.GenerativeComponents.GCScript.GCTypes;
using Bentley.GenerativeComponents.GCScript.NameScopes;
using Bentley.GenerativeComponents.GCScript.ReflectedNativeTypeSupport;
using Bentley.GenerativeComponents.GeneralPurpose;
using Bentley.GenerativeComponents.MicroStation;
using Bentley.GenerativeComponents.Nodes;
using Bentley.Interop.MicroStationDGN;

using CERVER.Hardware.Network;

//using Bentley.Wrapper;

namespace Bentley.GenerativeComponents.Nodes.Specific
{
    class UdpState
    {
        public IPEndPoint endPoint;
        public UdpClient Client;
    }

    public class Network : Node
    {


        private bool isInitial = true;
        private System.DateTime PrevTime ;
        private DispatcherTimer timer = new DispatcherTimer();

        private UdpClient udpc;
        private IPEndPoint ipep;
        private UdpState udpState = new UdpState();

        private string m_ipAddr = null;
        private int m_port;
        private string m_UDPMessage;

        //Network
        public const string noPort = "Port";
        public const string noAddress = "IPAddress";
        public const string noInterval = "UpdateInterval";
        public const string noValues = "Values";
        
        //techniques
        public const string NameOfDefaultTechnique = "UDPReceive";
        public const string NameOfUDPsend = "UDPSend";

        static private readonly NodeGCType s_gcTypeOfAllInstances = (NodeGCType)GCTypeTools.GetGCType(typeof(Network));
        static public NodeGCType GCTypeOfAllInstances
        {
            get { return s_gcTypeOfAllInstances; }
        }

        static private void GCType_AddAdditionalMembersTo(GCType gcType, NativeNamespaceTranslator namespaceTranslator)
        {

            {
                NodeTechnique method = gcType.AddDefaultNodeTechnique(NameOfDefaultTechnique, UDPReceive);

                //inputs
                method.AddArgumentDefinition(noPort, typeof(int), "8000", "The port to listen on", NodePortRole.TechniqueRequiredInput);
                method.AddArgumentDefinition(noAddress, typeof(string), "null", "The IP listen to, if null then it will listent on all IP addresses", NodePortRole.TechniqueOptionalInput);
                method.AddArgumentDefinition(noInterval, typeof(int), "25", "The update time in milliseconds", NodePortRole.TechniqueOptionalInput);

                //outputs
                method.AddArgumentDefinition(noValues, typeof(string), "", "Values of the coltroler", NodePortRole.TechniqueOutputOnly);
            }
            {
                NodeTechnique method = gcType.AddNodeTechnique(NameOfUDPsend, UDPSend);

                //inputs
                method.AddArgumentDefinition(noPort, typeof(int), "null", "The port to listen on", NodePortRole.TechniqueRequiredInput);
                method.AddArgumentDefinition(noAddress, typeof(string), "null", "The IP listen to, if null then it will listent on all IP addresses", NodePortRole.TechniqueRequiredInput);
                method.AddArgumentDefinition(noValues, typeof(string), "null", "Values of the coltroler", NodePortRole.TechniqueRequiredInput);
                
            }
         

        }
        
        static private NodeTechniqueResult UDPReceive(Node node, IGCEnvironment gcEnvironment, NameCatalog nameCatalog, NodeScopeUpdateReason updateReason)
        {
            Network netNode = (Network)node;
            if(netNode.Port != null)
                if (!netNode.Port.Equals(netNode.m_port)) netNode.isInitial = true;
            if (netNode.IPAddr != null)
                if (!netNode.IPAddr.Equals(netNode.m_ipAddr)) netNode.isInitial = true;


            if (netNode.isInitial)
            {
                netNode.SetupUDPReceive();
                netNode.isInitial = false;
            }
            
            return NodeTechniqueResult.Success;
        }

        static private NodeTechniqueResult UDPSend(Node node, IGCEnvironment gcEnvironment, NameCatalog nameCatalog, NodeScopeUpdateReason updateReason)
        {
            Network netNode = (Network)node;
            if (netNode.Port != null && netNode.IPAddr != null && netNode.UDPMessage != null && netNode.DoSendValues)
            {
                if (!netNode.IPAddr.Equals(netNode.m_ipAddr) || !netNode.Port.Equals(netNode.m_port) || netNode.ipep == null) 
                    netNode.ipep = new IPEndPoint(IPAddress.Parse(netNode.IPAddr), netNode.Port);

                byte[] message = System.Text.Encoding.ASCII.GetBytes(netNode.UDPMessage);
                netNode.udpc.Send(message, message.Length, netNode.ipep);
            }
    
            if (netNode.isInitial)
            {
                netNode.SetupUDPReceive();
                netNode.isInitial = false;
            }

            return NodeTechniqueResult.Success;
        }
        /***************/
        

        // ======================================== end of static members ========================================


        #region Input & output properties
        public int interval
        {
            get { return ActiveNodeState.UpdateIntervalProperty.GetNativeValue<int>(); }
            set { ActiveNodeState.UpdateIntervalProperty.SetNativeValueAndInputExpression(value); }
        }
        public string IPAddr
        {
            get { return ActiveNodeState.AddressProp.GetNativeValue<string>(); }
            set { ActiveNodeState.AddressProp.SetNativeValueAndInputExpression(value); }
        }

        public string UDPMessage
        {
            get { return ActiveNodeState.ValuesProp.GetNativeValue<string>(); }
            set { if (value != null)  ActiveNodeState.ValuesProp.SetNativeValueAndInputExpression(value); }
        }
        public int Port
        {
            get { return ActiveNodeState.PortProp.GetNativeValue<int>(); }
            set { ActiveNodeState.PortProp.SetNativeValueAndInputExpression(value); }
        } 
        #endregion

        #region My Functions

        public void ReceiveCallback(IAsyncResult ar)
        {

            Byte[] receiveBytes = udpc.EndReceive(ar, ref ipep);
            string receiveString = Encoding.ASCII.GetString(receiveBytes);
            m_UDPMessage = receiveString;

        }
      
        private bool IsValidIP(string ip)
        {
            IPAddress address;

            bool isok = IPAddress.TryParse(ip, out address);
            if (isok) IPAddr = address.ToString();

            return isok;

        }

        private void SetupUDPReceive()
        {
            timer.Tick -= timer_Tick; ;
            if (udpc != null) udpc.SafeDispose();

            if (IPAddr == null)
                ipep = new IPEndPoint(IPAddress.Any, Port);
            else
            {
                if (IsValidIP(IPAddr))
                {
                    ipep = new IPEndPoint(IPAddress.Parse(IPAddr), Port);
                }
            }

            udpc = new UdpClient(ipep);

            udpState.Client = udpc;
            udpState.endPoint = ipep;

            timer.Tick += timer_Tick; ;

            //udpc.BeginReceive(new AsyncCallback(ReceiveCallback), udpState );

        }
 
        void timer_Tick(object sender, EventArgs e)
        {
            if (udpc.Available>0)
            {
                var message = udpc.Receive(ref ipep);
                m_UDPMessage = Encoding.ASCII.GetString(message);

                int timeDif = System.DateTime.Now.Subtract(PrevTime).Milliseconds;
                if (timeDif > interval)
                {
                    UDPMessage = m_UDPMessage;
                    UpdateNodeTree();
                    PrevTime = System.DateTime.Now;
                }

            }

        } 
        #endregion

        #region WPF Binding
        // wpf bindings
        private bool m_DoReceiveValues = false;
        private bool m_DoSendValues = false;

        public bool DoReceiveValues
        {
            get { return m_DoReceiveValues; }
            set
            {
                m_DoReceiveValues = value;
                if (value)  
                    timer.Start();
                else
                    timer.Stop();
            }
        }
        public bool DoSendValues
        {
            get { return this.m_DoSendValues; }
            set
            {
                this.m_DoSendValues = value;
                this.OnPropertyChanged("SendValIsChecked");
            }
        }

        //end wpf bingings 
        #endregion

        #region Template Code
        public Network
        (
            NodeGCType gcType,
            INodeScope parentNodeScope,
            INameScope parentNameScope,
            string initialBasicName
        )
            : base(gcType, parentNodeScope, parentNameScope, initialBasicName)
        {
            Debug.Assert(gcType == s_gcTypeOfAllInstances);

        }

        public override Type TypeOfCustomViewContent(NodeCustomViewContext context)  // INode.TypeOfCustomViewBody
        {
            return typeof(GCNControl);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                timer.Stop();
                udpc.SafeDispose();
                
            }
            base.Dispose(disposing);
        }

        internal new NodeState ActiveNodeState
        {
            get { return (NodeState)base.ActiveNodeState; }
        }

        protected override Node.NodeState GetInitialNodeState(NodeScopeState parentNodeScopeState, string parentNodeInitialBasicName, NodeTechniqueDetermination initialActiveTechniqueDetermination)
        {
            return new NodeState(this, parentNodeScopeState, parentNodeInitialBasicName, initialActiveTechniqueDetermination);
        } 
        #endregion
        
        public new class NodeState : Node.NodeState
        {

            internal readonly NodeProperty UpdateIntervalProperty;
            internal readonly NodeProperty AddressProp;
            internal readonly NodeProperty ValuesProp;
            internal readonly NodeProperty PortProp;
  



            internal protected NodeState(Network parentNode, NodeScopeState parentNodeScopeState, string parentNodeInitialBasicName, NodeTechniqueDetermination initialActiveTechniqueDetermination)
                : base(parentNode, parentNodeScopeState, parentNodeInitialBasicName, initialActiveTechniqueDetermination)
            {
                // This constructor is called when the parent node is created.
                // To create each property, we call AddProperty (rather to GetProperty).
                

                UpdateIntervalProperty = AddProperty(noInterval);
                AddressProp = AddProperty(noAddress);
                ValuesProp = AddProperty(noValues);
                PortProp = AddProperty(noPort);

            }

         

            protected NodeState(NodeState source, NodeScopeState parentNodeScopeState)
                : base(source, parentNodeScopeState)  // For cloning.
            {
                // This constructor is called whenever the node state is copied.
                // To copy each property, we call GetProperty (rather than AddProperty).

                UpdateIntervalProperty = GetProperty(noInterval);
                AddressProp = GetProperty(noAddress);
                ValuesProp = GetProperty(noValues);
                PortProp = GetProperty(noPort);

            }

            protected new Network ParentNode
            {
                get { return (Network)base.ParentNode; }
            }

            public override Node.NodeState Clone(NodeScopeState newParentNodeScopeState)
            {
                return new NodeState(this, newParentNodeScopeState);
            }


        }
    }
}
