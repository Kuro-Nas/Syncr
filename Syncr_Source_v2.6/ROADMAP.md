# Syncr Edge Roadmap

## Version 3.0 (Planned for Next Week)

### Industrial Protocol Expansion (OT)
- **Protocol Support**:
    - [ ] Expand `ConnectionType` and associated settings in `MachineModel.cs`.
    - [ ] Maintain and integrate existing protocols:
        - **Modbus TCP** (Existing)
        - **Modbus RTU** (Existing)
    - [ ] Implement new drivers and handlers for:
        - **CANbus**: Automotive and specialized industrial sensors.
        - **EtherNet/IP**: Rockwell/Allen-Bradley ecosystem.
        - **CC-Link / CC-Link IE / TSN**: Mitsubishi Electric ecosystem.
        - **EtherCAT**: High-performance real-time Ethernet.
        - **Profibus**: Legacy serial-based industrial communication.
- **Dynamic UI Settings**:
    - [ ] Update Machine Editor UI to show protocol-specific fields (e.g., Node ID for CAN, IP for Profinet).

### UI/UX Refinements
- **Color Palette Variety**: 
    - [ ] Redesign the 48-preset color palette in `ColorPickerViewModel.cs`.
    - [ ] Remove redundant "shades of white" and light pastels.
    - [ ] Add more vibrant, distinct, and deeper industrial tones.

### Pi-Specific Security (Kiosk Mode)
- **Strict Full-Screen**:
    - [ ] Implementation of "Kiosk Mode" exclusively for Linux/Pi ARM64.
    - [ ] Disable taskbar, Alt+Tab, and other system-switching shortcuts.
- **Terminal Unlock**:
    - [ ] Protect application exit/minimization with a terminal-based password prompt.
    - [ ] **STRICT RULE**: Feature must NOT be present in Windows builds.

### Branding & Deployment
- **Kuro Syncr Branding**:
    - [x] Rename Desktop shortcut and installer identity.
    - [ ] Investigate and fix Linux icon visibility issues.
- **Deployment Script Organization**:
    - [x] Separate versioned scripts: `deploy_pi_v26.sh` and `deploy_pi_v30.sh`.
