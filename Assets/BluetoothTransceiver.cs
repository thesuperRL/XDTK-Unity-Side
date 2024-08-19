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
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.IO;
using UnityEngine;
using XDTK32Feet;
using System;
using System.Runtime.Remoting.Messaging;
using System.Text.RegularExpressions;

namespace Google.XR.XDTK
{
    public class BluetoothTransceiver : Transceiver
    {
        public GameObject DevicePrefab;
        private string AndroidDeviceName = "XDTKAndroid3";
        private string receivedMACaddress;

        private Stream bluetoothStream;
        private String pastMessageEnding = "";
        private byte[] receivedBytes = new byte[4096];
        private string receivedString;
        private bool requestedThisPacket = false;

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
            // Initialize Transceiver
            base.Initialize();

            // Initialize Connection
            InitializeBluetoothConnection(true);
            receivedMACaddress = XDTK32Feet.XDTK32Feet.mDevice.DeviceAddress.ToString();

            // Acquire the stream
            FindBluetoothStream();

            RemoveDuplicateAddressesAndIDs();
            // For now, use one device
            InitializeDevices();
        }

        // Update is called once per frame
        public override void Update()
        {
            // Handle creating new device (must be done on main thread)
            if (creatingNewDevice)
            {
                CreateNewDevice(IDforCreatedDevice, addressforCreatedDevice, infoMessageforCreatedDevice);
                creatingNewDevice = false;
            }
            if (bluetoothStream != null)
            {
                int bytesRead = ReadNextMessage();
                ProcessMessage(bytesRead);
            }
        }


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

        void FindBluetoothStream()
        {
            bluetoothStream = XDTK32Feet.XDTK32Feet.stream;
        }

        void InitializeBluetoothConnection(bool isUsingPicker)
        {
            // Establish connection to other device
            if (isUsingPicker)
            {
                XDTK32Feet.XDTK32Feet.GenerateConnectionUsingPicker();
            }
            else
            {
                XDTK32Feet.XDTK32Feet.GenerateConnectionToDevice(AndroidDeviceName);
            }

            if (XDTK32Feet.XDTK32Feet.mDevice != null)
            {
                Debug.Log("[BluetoothTransceiver] Bluetooth Device Connected.");
            }
            else
            {
                Debug.Log("[BluetoothTransceiver] Bluetooth Failed to Connect to Device.");
            }
        }

        // Add Devices in scene to to database 
        void InitializeDevices()
        {
            Debug.Log("[BluetoothTransceiver] attempting to initialize devices");
            foreach (Device d in FindObjectsOfType<Device>())
            {
                // Add to database
                if (!string.IsNullOrEmpty(d.Address)) devicesByAddress.Add(d.Address, d);
                if (d.ID >= 0) devicesByID.Add(d.ID, d);
                devices.Add(d);
                if (debugPrint) Debug.Log("[BluetoothTransceiver] Registering device " + d.DeviceName);
            }
        }

        int ReadNextMessage()
        {
            if (debugPrint) Debug.Log("[BluetoothTransceiver] Reading Next Message");
            // a buffer of 2048 seems to be enough, seeing as the peak was like a bit more than 1024 during testing
            // even if info is cut off, we do some programming later to grab the last fragment and add it to the first of the next read
            return bluetoothStream.Read(receivedBytes, 0, receivedBytes.Length);
        }

        // Callback executed when Bluetooth successfully reads a value from the stream
        // "Message received" callback (Android --> Unity)
        void ProcessMessage(int bytesRead)
        {
            if (debugPrint) Debug.Log("[BluetoothTransceiver] Processing Received packet");

            // Convert message to string
            receivedString = System.Text.Encoding.UTF8.GetString(receivedBytes, 0, bytesRead);
            string[] subpackets = receivedString.Split(" | ");
            receivedMACaddress = XDTK32Feet.XDTK32Feet.mDevice.DeviceAddress.ToString();

            requestedThisPacket = false;

            for (int i = 0; i < subpackets.Length - 1; i++)
            {
                string currentMessage = subpackets[i];

                // Handle device discovery
                // Only ask for info every 60 packets because there's a delay for it to get here (abt 1 second)
                if (/*(requestedThisPacket || splitline[1] == "DEVICEINFO") && */ !registeredAddresses.Contains(receivedMACaddress))
                {
                    // If  we haven't heard from this device before, handle adding it
                    Debug.Log("[BluetoothTransceiver] Attempting to add device: " + receivedMACaddress);
                    HandleAddDevice(currentMessage, receivedMACaddress);
                    requestedThisPacket = true;
                }

                if (debugPrint) Debug.Log("[BluetoothTransceiver] Received packet: " + currentMessage);

                if (registeredAddresses.Contains(receivedMACaddress))
                {
                    base.RouteMessageToDevice(currentMessage, receivedMACaddress);
                }

                // Send HEARTBEAT back to sender
                SendMessage("HEARTBEAT");
            }
        }

        // Add 
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
                        // assign new ID
                        while (devicesByID.ContainsKey(nextID)) nextID++;
                        d.ID = nextID;
                        nextID++;
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
                            if (!devicesByID.ContainsKey(d.ID)) devicesByID.Add(d.ID, d);
                            if (!devicesByAddress.ContainsKey(d.Address)) devicesByAddress.Add(d.Address, d);
                            registeredAddresses.Add(d.Address);
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
                            while (devicesByID.ContainsKey(nextID)) nextID++;
                            d.ID = nextID;
                            nextID++;

                            // Add to databases
                            if (!devicesByID.ContainsKey(d.ID)) devicesByID.Add(d.ID, d);
                            if (!devicesByAddress.ContainsKey(d.Address)) devicesByAddress.Add(d.Address, d);
                            registeredAddresses.Add(d.Address);
                            Debug.Log("[BluetoothTransceiver] Added Device " + d.ID + ": " + address);
                            return;
                        }
                    }
                }

                // If we reach this point in the script, that means we need to instantiate a new Device (on the main thread)
                Debug.Log("[BluetoothTransceiver] Instantiating new Device prefab.");
                if (!creatingNewDevice)
                {
                    creatingNewDevice = true;

                    // assign address
                    addressforCreatedDevice = address;

                    // assign new ID
                    while (devicesByID.ContainsKey(nextID)) nextID++;
                    IDforCreatedDevice = nextID;
                    nextID++;

                    // store message
                    infoMessageforCreatedDevice = message;
                }
            }
            // otherwise, request DEVICE_INFO from this device if there's been 4 packets since the last request
            else
            {
                SendMessage("WHOAREYOU");
                Debug.Log("[BluetoothTransceiver] Sent device info request to: " + address);
            }
        }

        // Create a new Device prefab and store its information
        void CreateNewDevice(int newID, string newAddress, string newInfoMessage)
        {
            // Create Device
            GameObject d_object = Instantiate(DevicePrefab);
            Device d = d_object.GetComponent<Device>();
            d.ID = IDforCreatedDevice;
            d.Address = addressforCreatedDevice;

            // Add to database
            devices.Add(d);
            registeredAddresses.Add(d.Address);
            if (!devicesByID.ContainsKey(d.ID)) devicesByID.Add(d.ID, d);
            if (!devicesByAddress.ContainsKey(d.Address)) devicesByAddress.Add(d.Address, d);

            // Route DEVICE_INFO message to newly created device
            base.RouteMessageToDevice(infoMessageforCreatedDevice, d.Address);
        }

        // Send message to specific IP address (Unity --> Android)
        public void SendMessage(string message)
        {
            byte[] data = Encoding.UTF8.GetBytes(message);
            bluetoothStream.Write(data, 0, data.Length);
        }

        void OnDestroy()
        {
            XDTK32Feet.XDTK32Feet.Close();
        }
    }
}