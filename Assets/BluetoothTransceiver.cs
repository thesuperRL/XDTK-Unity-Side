// Copyright 2024 Google LLC
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     https://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System.Collections.Generic;
using System.Text;
using System.IO;
using UnityEngine;
using XDTK32Feet;
using System;
using System.Threading;
using Unity.Tutorials.Core.Editor;
using System.Net.Mail;

namespace Google.XR.XDTK
{
    public class BluetoothTransceiver : Transceiver
    {
        public GameObject devicePrefab;
        public string keyToActivateBluetoothSelector = "space";

        private Dictionary<string, BluetoothAgent> addressToBluetoothAgents = new Dictionary<string, BluetoothAgent>();

        // Device Discovery
        private int nextID = 0;
        private bool creatingNewDevice = false;
        private int IDforCreatedDevice = -1;
        private string infoMessageforCreatedDevice = "";

        // Debug
        public bool debugPrint = false;

        // Start is called before the first frame update
        public override void Start()
        {
            base.Initialize();
        }

        // Update is called once per frame
        public override void Update()
        {
            // Allow for additional connections if user needs 
            if (Input.GetKeyDown(keyToActivateBluetoothSelector))
            {
                BluetoothAgent agent = new BluetoothAgent(this);
                // Generate a popup borrowing this Unity device's native Bluetooth picker
                if (agent.GenerateBluetoothPopup())
                {
                    Debug.Log("[BluetoothTransceiver] Connection Succeeded");
                }
                else
                {
                    Debug.Log("[BluetoothTransceiver] Connection Failed");
                }
            }
        }

        int GetNextDeviceID()
        {
            // Assign new ID
            while (devicesByID.ContainsKey(nextID)) nextID++;
            return nextID++; // Adds 1 to it after its creation
        }

        // Add device into "registeredAddresses" and "devices" until device sends back DEVICE_INFO message
        void HandleAddDevice(string message, string address)
        {
            // parse message
            string[] strings = message.Split(',');
            long timeStamp = long.Parse(strings[0]);

            // get message header
            if (!(strings.Length > 1)) return;
            string header = strings[1];

            // if this is a DEVICE_INFO message, add the device
            if (header == "DEVICE_INFO")
            {
                if (registeredAddresses.Contains(address))   return; //ignore duplicated DEVICE_INFO messages because there are many requests

                Debug.Log("[BluetoothTransceiver] Received DEVICE_INFO message from: " + address + "; message: " + message);

                registeredAddresses.Add(address);

                // Route DEVICE_INFO message to newly created device so to update key device details
                RouteMessageToDevice(message, address);
            }
            // otherwise, request DEVICE_INFO from this device
            else
            {
                // ask for details to later receive it
                this.addressToBluetoothAgents[address].SendMessage("WHOAREYOU");
                Debug.Log("[BluetoothTransceiver] Sent device info request to: " + address);
            }
        }
        void OnDestroy()
        {
            // remove all connected receivers
            foreach (KeyValuePair<string, BluetoothAgent> pair in addressToBluetoothAgents)
            {
                pair.Value.Destroy();
            }
        }


        public class BluetoothAgent
        /*  
            Each bluetooth device has its own stream to receive information.
            To connect with multiple bluetooth devices, 
            a BluetoothAgent is used for each Bluetooth device. 
            Therefore, multiple Bluetooth devices can connect with server side independently. 
        */
        {
            private BluetoothTransceiver manager;
            private string MACAddress;
            private Device device;
            public bool connected = false;

            private XDTK32Feet.BluetoothReceiver receiver = new BluetoothReceiver();
            public Stream bluetoothStream;
            private byte[] receivedBytes = new byte[1024];
            private string receivedString;
            private string pastHalfPacket = "";

            // acquire manager to trigger functions and access data structures within it
            public BluetoothAgent(BluetoothTransceiver manager)
            {
                this.manager = manager;
            }

            // create a popup to choose a device
            public bool GenerateBluetoothPopup()
            {
                // Initialize Connection
                if (InitializeBluetoothConnection())
                {
                    // Bluetooth device MAC Address, ready after GenerateConnectionUsingPicker()
                    MACAddress = receiver.mDevice.DeviceAddress.ToString(); 

                    // Create a new device prefab
                    CreateNewDevice();

                    // Start a new thread to read information from the stream
                    Thread readThread = new Thread(new ThreadStart(AsyncRead));
                    readThread.Start();

                    return true;
                }
                else
                {
                    return false;
                }
            }

            // Initialize the connection to the selected device
            public bool InitializeBluetoothConnection()
            {
                // Use the picker method within the receiver
                receiver.GenerateConnectionUsingPicker();

                // If the other device in the pair exists
                if (receiver.mDevice != null)
                {
                    // Access its stream and request information. Mark self as connected
                    Debug.Log("[BluetoothTransceiver] Bluetooth Device Connected.");
                    bluetoothStream = receiver.stream;
                    SendMessage("WHOAREYOU");
                    connected = true;
                }
                else
                {
                    // Notify that connection failed
                    Debug.Log("[BluetoothTransceiver] Bluetooth Failed to Connect to Device.");
                }

                return connected;
            }

            // Create a new Device prefab in Bluetooth Transceiver and store its information
            void CreateNewDevice()
            {
                if (manager.devicesByAddress.ContainsKey(MACAddress))
                {
                    // If there already contains an old device prefab with this address, just use that prefab
                    device = manager.devicesByAddress[MACAddress];
                    Debug.Log("MACAddress = " + MACAddress);
                }
                else
                {
                    // Create a Device prefab
                    // Store it locally, this way we don't need to access RouteMessageToDevice to edit details
                    GameObject d_object = Instantiate(manager.devicePrefab);
                    device = d_object.GetComponent<Device>();
                    device.ID = manager.GetNextDeviceID();
                    //MACAddress = MACAddress + "-" + device.ID; //temporary distinction to test multiple devices
                    device.Address = MACAddress;
                    Debug.Log("MACAddress = " + MACAddress);

                    // Bluetooth Device has ID and address as long as it's created.
                    //  - Add to "devices" list
                    //  - Add to HashMap by ID "devicesByID"
                    //  - Add to HashMap by address "devicesByAddress"
                    //  - Add to HashMap by address "addressToBluetoothAgents"
                    // Do NOT add to "registeredAddresses" until device sends back DEVICE_INFO message
                    manager.devices.Add(device);
                    manager.devicesByID.Add(device.ID, device);
                    manager.devicesByAddress.Add(device.Address, device);
                    manager.addressToBluetoothAgents.Add(device.Address, this);
                }
            }

            // Read the next packet sent within the stream.
            int ReadNextPacket()
            {
                if (manager.debugPrint) Debug.Log("[BluetoothTransceiver] Reading Next Message");
                // this should match the packet size as indicated on the phone end. From testing, it appears that it works smoother when there's less to read
                // even if info is cut off, we do some programming later to grab the last fragment and add it to the first of the next read
                return bluetoothStream.Read(receivedBytes, 0, receivedBytes.Length);
            }

            // Asynchronously repeatedly reads the packet and asks to process it
            void AsyncRead()
            {
                // Do this forever
                while (true)
                {
                    int bytesRead = ReadNextPacket();
                    ProcessMessage(bytesRead);
                }
            }

            // Processes the message.
            // Unfortunately BT has no callback like UDP for reads
            void ProcessMessage(int bytesRead)
            {
                if (manager.debugPrint) Debug.Log("[BluetoothTransceiver] Processing Received packet");

                // Convert message to string
                receivedString = Encoding.UTF8.GetString(receivedBytes, 0, bytesRead);
                // obtain the length of the packet
                string[] subpackets = receivedString.Split("|");

                // there is a half packet so we take the first one and append.
                // Three cases here:
                // - Past packet transmitted whole: therefore the pastHalfPacket variable contains an empty string,
                //   added to another empty string would continue (be ignored) at the IsNullOrWhiteSpace line.
                // - Past packet transmitted halved: adding to the next read other half of the packet fixes that
                // - Past packet transmitted without the last separator: The pastHalfPacket is a whole command. 
                //   The first value in the next packet is a separator, thus the command stored in pastHalfPacket
                //   is the first one executed on the next read.
                // All three cases work out.
                subpackets[0] = pastHalfPacket + subpackets[0];

                // grab this to see when the read should end
                int endIndex = subpackets.Length;

                // Send HEARTBEAT back to sender
                // This indicates a finished read and when Android should send the next full packet
                SendMessage("HEARTBEAT");

                // For each subpacket except the last (potentially halved subpacket)
                for (int i = 0; i < endIndex - 1; i++)
                {
                    // Grab the current message, if it's whitespace then just ignore it
                    string currentMessage = subpackets[i];
                    if (currentMessage.IsNullOrWhiteSpace()) continue;

                    if (manager.debugPrint) Debug.Log("[BluetoothTransceiver] Received packet: " + currentMessage);

                    // Handle device discovery
                    if (!manager.registeredAddresses.Contains(MACAddress))
                    {
                        // If we haven't heard from this device before, handle adding it
                        Debug.Log("[BluetoothTransceiver] Attempting to add device: " + MACAddress);
                        manager.HandleAddDevice(currentMessage, MACAddress);
                    }

                    if (manager.registeredAddresses.Contains(MACAddress))
                    {
                        // Unlike UDPTransciver, this bluetooth agent object is created 1:1 with device prefabs. 
                        // Therefore, it doesn't have to route current message to device through Transceiver (just does extra work)
                        // So we just ask its corresponding prefab to handle it.
                        device.ParseData(currentMessage);
                    }
                }
                
                // This records the last half packet to be added later.
                pastHalfPacket = subpackets[^1];
            }

            // Send message to specific MAC address (Unity --> Android)
            public void SendMessage(string message)
            {
                //if(message != "HEARTBEAT") Debug.Log("SendMessage: " + message);
                byte[] data = Encoding.UTF8.GetBytes(message);
                bluetoothStream.Write(data, 0, data.Length);
            }

            // Close the transceiver
            public void Destroy()
            {
                receiver.Close();
            }
        }
    }
}