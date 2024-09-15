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
        public string keyToActivateBluetoothSelector;

        private List<BluetoothAgent> bluetoothAgents = new List<BluetoothAgent>();
        private Dictionary<string, BluetoothAgent> addressToBluetoothAgents = new Dictionary<string, BluetoothAgent>();

        // Device Discovery
        private int nextID = 0;
        private bool creatingNewDevice = false;
        public string addressforCreatedDevice = "";
        private int IDforCreatedDevice = -1;
        private string infoMessageforCreatedDevice = "";

        // Debug
        public bool debugPrint = false;

        // Start is called before the first frame update
        public override void Start()
        {
            base.Initialize();
            keyToActivateBluetoothSelector = "space";
        }

        // Update is called once per frame
        public override void Update()
        {
            // Allow for additional connections if user needs 
            if (Input.GetKeyDown(keyToActivateBluetoothSelector))
            {
                BluetoothAgent agent = new BluetoothAgent(this);
                if (agent.GenerateBluetoothPopup())
                {
                    bluetoothAgents.Add(agent);
                }
            }
        }

        int GetNextDeviceID()
        {
            // assign new ID
            while (devicesByID.ContainsKey(nextID)) nextID++;
            return nextID++;
        }

        // Add device into "registeredAddresses" and "devices" until device send back DEVICE_INFO message
        void HandleAddDevice(string message, string address)
        {
            //Debug.Log("[HandleAddDevice] message: " + message + ";address: " + address);
            // parse message
            string[] strings = message.Split(',');
            long timeStamp = long.Parse(strings[0]);

            // get message header
            if (!(strings.Length > 1)) return;
            string header = strings[1];

            // if this is a DEVICE_INFO message, add the device
            if (header == "DEVICE_INFO")
            {
                if (registeredAddresses.Contains(address))   return; //ignore duplicated DEVICE_INFO messages

                Debug.Log("[BluetoothTransceiver] Received DEVICE_INFO message from: " + address + "; message: " + message);

                registeredAddresses.Add(address);

                // Route DEVICE_INFO message to newly created device so to update following:  
                //      - DeviceName, Size_px, Size_in, Size_m
                RouteMessageToDevice(message, address);
            }
            // otherwise, request DEVICE_INFO from this device
            else
            {
                this.addressToBluetoothAgents[address].SendMessage("WHOAREYOU");
                Debug.Log("[BluetoothTransceiver] Sent device info request to: " + address);
            }
        }


        public class BluetoothAgent
        /*  Each bluetooth device has its own stream buffer. 
            To connect with multiple bluetooth device, 
            a BluetoothAgent is being used for each bluetooth device. 
            Therefore, multiple bluetooth device can connect with server side independently. 
        */
        {
            private BluetoothTransceiver manager;
            private string MACAddress;
            private Device device;
            public bool connected = false;

            private XDTK32Feet.BluetoothReceiver receiver = new BluetoothReceiver();
            public Stream bluetoothStream;
            private byte[] receivedBytes = new byte[1000];
            private string receivedString;
            private string pastHalfPacket = "";

            public BluetoothAgent(BluetoothTransceiver manager)
            {
                this.manager = manager;
            }

            public Boolean GenerateBluetoothPopup()
            {
                // Initialize Connection
                if (InitializeBluetoothConnection())
                {
                    //Bluetooth device MAC Address, ready after GenerateConnectionUsingPicker()
                    MACAddress = receiver.mDevice.DeviceAddress.ToString(); 
                    //Debug.Log("GenerateBluetoothPopup Initial MAC Address: " + MACAddress);

                    CreateNewDevice();

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
            public Boolean InitializeBluetoothConnection()
            {
                // Establish connection to other device

                receiver.GenerateConnectionUsingPicker();

                if (receiver.mDevice != null)
                {
                    Debug.Log("[BluetoothTransceiver] Bluetooth Device Connected.");
                    bluetoothStream = receiver.stream;
                    Debug.Log("bluetoothStream:"+ bluetoothStream);
                    SendMessage("WHOAREYOU");
                    connected = true;
                }
                else
                {
                    Debug.Log("[BluetoothTransceiver] Bluetooth Failed to Connect to Device.");
                }

                return connected;
            }

            // Create a new Device prefab in Bluetooth Transceiver and store its information
            void CreateNewDevice()
            {
                // Create Device
                GameObject d_object = Instantiate(manager.devicePrefab);
                device = d_object.GetComponent<Device>();
                device.ID = manager.GetNextDeviceID();
                MACAddress = MACAddress + "-" + device.ID; //tentative change to test multi devices
                device.Address = MACAddress;
                Debug.Log("addressforCreatedDevice:" + manager.addressforCreatedDevice+ ";MACAddress="+ MACAddress);

                // Bluetooth Device has ID and address as long as it's created.
                //  - Add to "devices" list
                //  - Add to HashMap by ID "devicesByID"
                //  - Add to HashMap by address "devicesByAddress"
                //  - Add to HashMap by address "addressToBluetoothAgents"
                // Do NOT add to "registeredAddresses" until device send back DEVICE_INFO message
                manager.devices.Add(device);
                manager.devicesByID.Add(device.ID, device);
                manager.devicesByAddress.Add(device.Address, device);
                manager.addressToBluetoothAgents.Add(device.Address, this);

            }

            // Read the next packet sent within the stream.
            int ReadNextPacket()
            {
                if (manager.debugPrint) Debug.Log("[BluetoothTransceiver] Reading Next Message");
                // a buffer of 2048 seems to be enough, seeing as the peak was like a bit more than 1024 during testing
                // even if info is cut off, we do some programming later to grab the last fragment and add it to the first of the next read
                return bluetoothStream.Read(receivedBytes, 0, receivedBytes.Length);
            }

            // Asynchronously repeatedly reads the packet and asks to process it
            void AsyncRead()
            {
                while (true)
                {
                    int bytesRead = ReadNextPacket();
                    ProcessMessage(bytesRead);
                }
            }

            // Callback executed when Bluetooth successfully reads a value from the stream
            // "Message received" callback (Android --> Unity)
            void ProcessMessage(int bytesRead)
            {
                if (manager.debugPrint) Debug.Log("[BluetoothTransceiver] Processing Received packet");

                // Convert message to string
                receivedString = Encoding.UTF8.GetString(receivedBytes, 0, bytesRead);
                // obtain the length of the packet
                string[] subpackets = receivedString.Split("|");

                // there is a half packet so we take the first one and append
                subpackets[0] = pastHalfPacket + subpackets[0];

                int endIndex = subpackets.Length;

                // Send HEARTBEAT back to sender
                // This indicates a finished read and when Android should send the next full packet
                SendMessage("HEARTBEAT");

                // For each subpacket
                for (int i = 0; i < endIndex - 1; i++)
                {
                    // Grab the current message, if it's whitespace then just continue
                    string currentMessage = subpackets[i];
                    if (currentMessage.IsNullOrWhiteSpace()) continue;

                    if (manager.debugPrint) Debug.Log("[BluetoothTransceiver] Received packet: " + currentMessage);

                    // Handle device discovery
                    if (!manager.registeredAddresses.Contains(MACAddress))
                    {
                        // If  we haven't heard from this device before, handle adding it
                        //Debug.Log("[BluetoothTransceiver] Attempting to add device: " + MACAddress);
                        manager.HandleAddDevice(currentMessage, MACAddress);
                    }

                    if (manager.registeredAddresses.Contains(MACAddress))
                    {
                        //Unlike UDPTransciver, this bluetooth agent is 1:1 with device. 
                        //Therefore, it doesn't have to route current message to device through Transceiver.
                        //manager.RouteMessageToDevice(currentMessage, MACAddress);
                        device.ParseData(currentMessage);
                    }
                }
                
                pastHalfPacket = subpackets[^1];
            }

            // Send message to specific IP address (Unity --> Android)
            public void SendMessage(string message)
            {
                //if(message != "HEARTBEAT") Debug.Log("SendMessage: " + message);
                byte[] data = Encoding.UTF8.GetBytes(message);
                bluetoothStream.Write(data, 0, data.Length);
            }

            // Close the transceiver
            void OnDestroy()
            {
                receiver.Close();
            }
        }
    }
}