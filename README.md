# XDTK Bluetooth Unity Source Code
- Source code for the UnityPackage intended to enable Bluetooth for https://github.com/google/xdtk
- Uses DLL at https://github.com/thesuperRL/XDTK32Feet to accomplish Bluetooth detection and connection

## Requirements
Unity -> Edit -> Project Settings -> Player
- Scripting Backend: Mono
- API Compatibility Level: .NET Framework
- Active Input Handling: Has to involve the old one (can use Both)

## Set-up
- As of now, you would need to go to the DeviceManager prefab instance in XDTK-Sample Scene and replace the current UDP Transceiver with the Bluetooth Transceiver file. 
- Add the DeviceVisual prefab and it should be set up

## Usage
Run the code and it should work, asking you to pick a device. Before you pick the device, make sure that the device is connection button is on, and the device is on Bluetooth mode

## Known Limitations
Compared to the Wi-fi performance, it...
- Has a very laggy touch detector (Other attributes such as device rotation works perfectly)
- Can only connect to one device as opposed to multiple

### Peculiarities
- Has a lot of device requests be printed at the beginning. That is normal.
- If Bluetooth Android ends its connection before Unity game stops being played, it sends a lot of errors
- Bluetooth Android must be ended to connect again if it has previously been connected to an XDTK Unity device

## Contributors
- **Ryan C. Li**, Eastside Preparatory School
### With help from
- **Mar Gonzalez-Franco**, Google AR
- **Eric J. Gonzalez**, Google AR
