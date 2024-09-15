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
It should now work exactly the same as Wi-Fi connection!

### Peculiarities
- Has a lot of device requests be printed at the beginning. That is normal.
- If Bluetooth Android ends its connection before Unity game stops being played, it sends errors. If the device connects back, it maps to the correct prefab and errors don't have an effect.
- Bluetooth Android must be ended to connect again if it has previously been connected to an XDTK Unity device, even if the game stops running.

## Contributors
- **Ryan C. Li**, Eastside Preparatory School
### With help from
- **Mar Gonzalez-Franco**, Google AR
- **Eric J. Gonzalez**, Google AR
