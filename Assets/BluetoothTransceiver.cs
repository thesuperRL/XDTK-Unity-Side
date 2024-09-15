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

        private BluetoothAgent preparedAgent;
        private List<BluetoothAgent> bluetoothAgents = new List<BluetoothAgent>();
        private Dictionary<string, BluetoothAgent> addressToBluetoothAgents = new Dictionary<string, BluetoothAgent>();

        // Device Discovery
        private int nextID = 0;
        private bool creatingNewDevice = false;
        private string addressforCreatedDevice = "";
        private int IDforCreatedDevice = -1;
        private string infoMessageforCreatedDevice = "";

        // Debug
        public bool debugPrint = false;

        // Start is called before the first frame update
        public override void Start()
        {
            base.Start();
            PrepareNewAgent();
        }

        // Update is called once per frame
        public override void Update()
        {
            // Allow for additional connections if user needs 
            if (Input.GetKeyDown(keyToActivateBluetoothSelector))
            {
                preparedAgent.GenerateBluetoothPopup();
                if (preparedAgent.connected)
                {
                    bluetoothAgents.Add(preparedAgent);
                    PrepareNewAgent();
                }
            }
        }

        void PrepareNewAgent()
        {
            preparedAgent = new BluetoothAgent(this);

            // Remove duplicated Devices
            //RemoveDuplicateAddressesAndIDs();
            // For now, use one device
            InitializeDevices();
        }


        // Identical to the Wi-Fi code, also removes identical devices
        void RemoveDuplicateAddressesAndIDs()
        {
            List<string> addresses = new List<string>();
            List<int> IDs = new List<int>();
            foreach (Device d in FindObjectsOfType<Device>())
            {
                // Check for duplicate addresses
                if (!string.IsNullOrEmpty(d.Address) && addresses.Contains(d.Address))
                {
                    Debug.LogWarning("[BluetoothTransceiver] Duplicate device address " + d.Address + ". Removing one.");
                    d.Address = null;
                }
                else
                {
                    addresses.Add(d.Address);
                }

                // Check for duplicate IDs
                if (d.ID > 0 && IDs.Contains(d.ID))
                {
                    Debug.LogWarning("[BluetoothTransceiver] Duplicate device ID " + d.ID + ". Removing one.");
                    d.ID = -1;
                }
                else
                {
                    IDs.Add(d.ID);
                }
            }
        }

        // Add Devices in scene to to database 
        void InitializeDevices()
        {
            Debug.Log("[BluetoothTransceiver] attempting to initialize devices");
            //foreach (Device d in FindObjectsOfType<Device>())
            //{
            //    // Add to database
            //    if (!string.IsNullOrEmpty(d.Address) && !devicesByAddress.ContainsKey(d.Address)) devicesByAddress.Add(d.Address, d);
            //    if (d.ID >= 0 && !devicesByID.ContainsKey(d.ID)) devicesByID.Add(d.ID, d);
            //    devices.Add(d);
            //    if (debugPrint) Debug.Log("[BluetoothTransceiver] Registering device " + d.DeviceName);
            //}
        }

        int GetNextDeviceID()
        {
            // assign new ID
            while (devicesByID.ContainsKey(nextID)) nextID++;
            return nextID++;
        }

        void AddToDatabase(Device device)
        {
            // Add to database
            registeredAddresses.Add(device.Address);
            if (!devicesByID.ContainsKey(device.ID)) devicesByID.Add(device.ID, device);
            if (!devicesByAddress.ContainsKey(device.Address)) devicesByAddress.Add(device.Address, device);

            Debug.Log(devices);
            Debug.Log(devicesByAddress);
            Debug.Log(devicesByID);
            Debug.Log(registeredAddresses);
        }

        // Add a device
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
                Debug.Log("[BluetoothTransceiver] Received DEVICE_INFO message from: " + address);

                // Check if this MAC address has been specified by any Device scripts in the scene
                if (devicesByAddress.ContainsKey(address))
                {
                    // if so, add to registered addresses
                    registeredAddresses.Add(address);

                    // check if we need to generate an ID or if it has been specified
                    Device d = devicesByAddress[address];
                    if (d.ID < 0)
                    {
                        d.ID = GetNextDeviceID();
                    }

                    // Add to ID database if needed
                    if (!devicesByID.ContainsKey(d.ID))
                    {
                        devicesByID.Add(d.ID, d);
                    }

                    Debug.Log("[BluetoothTransceiver] Added Device " + d.ID + ": " + address);
                    return;
                }
                else
                {
                    // check if there are any devices in the scene with a specified MAC (and no specified address)
                    foreach (Device d in devices)
                    {
                        // if we come across one, add it
                        if (d.ID >= 0 && string.IsNullOrEmpty(d.Address))
                        {
                            d.Address = address;

                            // Add to databases
                            AddToDatabase(d);
                            Debug.Log("[BluetoothTransceiver] Added Device " + d.ID + ": " + address);
                            return;
                        }
                    }

                    // if there are any devices in the scene, even with no specified ID or address
                    // assign those before instantiating any others
                    foreach (Device d in devices)
                    {
                        // if we come across one, add it
                        if (d.ID < 0 && string.IsNullOrEmpty(d.Address))
                        {
                            d.Address = address;

                            // assign new ID
                            d.ID = GetNextDeviceID();

                            // Add to databases
                            AddToDatabase(d);
                            Debug.Log("[BluetoothTransceiver] Added Device " + d.ID + ": " + address);
                            return;
                        }
                    }
                }


                // Route DEVICE_INFO message to newly created device
                Debug.Log(message);
                RouteMessageToDevice(message, address);
            }
            // otherwise, request DEVICE_INFO from this device
            else
            {
                SendMessage("WHOAREYOU");
                Debug.Log("[BluetoothTransceiver] Sent device info request to: " + address);
            }
        }

        public class BluetoothAgent
        {
            private BluetoothTransceiver manager;
            private string MACAddress;
            private Device device;
            public bool connected = false;

            private XDTK32Feet.BluetoothReceiver receiver = new BluetoothReceiver();
            private Stream bluetoothStream;
            private byte[] receivedBytes = new byte[1000];
            private string receivedString;
            private string pastHalfPacket = "";

            public BluetoothAgent(BluetoothTransceiver manager)
            {
                this.manager = manager;
            }

            public void GenerateBluetoothPopup()
            {
                // Initialize Connection
                Debug.Log("GenerateBluetoothPopup 1");
                InitializeBluetoothConnection();
                Debug.Log("GenerateBluetoothPopup 2");
                MACAddress = receiver.mDevice.DeviceAddress.ToString();
                Debug.Log("GenerateBluetoothPopup 3: " + MACAddress);

                CreateNewDevice();
                Debug.Log("GenerateBluetoothPopup 4");

                Thread readThread = new Thread(new ThreadStart(AsyncRead));
                readThread.Start();
            }

            // Initialize the connection to the selected device
            void InitializeBluetoothConnection()
            {
                // Establish connection to other device

                receiver.GenerateConnectionUsingPicker();

                if (receiver.mDevice != null)
                {
                    Debug.Log("[BluetoothTransceiver] Bluetooth Device Connected.");
                    bluetoothStream = receiver.stream;
                    connected = true;
                }
                else
                {
                    Debug.Log("[BluetoothTransceiver] Bluetooth Failed to Connect to Device.");
                }
            }

            // Create a new Device prefab and store its information
            void CreateNewDevice()
            {
                // Create Device
                GameObject d_object = Instantiate(manager.devicePrefab);
                device = d_object.GetComponent<Device>();
                device.ID = manager.GetNextDeviceID();
                device.Address = MACAddress;



                manager.devices.Add(device);
                manager.AddToDatabase(device);
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
                        Debug.Log("[BluetoothTransceiver] Attempting to add device: " + MACAddress);
                        manager.HandleAddDevice(currentMessage, MACAddress);
                    }

                    if (manager.registeredAddresses.Contains(MACAddress))
                    {
                        manager.RouteMessageToDevice(currentMessage, MACAddress);
                    }
                }

                pastHalfPacket = subpackets[^1];
            }

            // Send message to specific IP address (Unity --> Android)
            public void SendMessage(string message)
            {
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