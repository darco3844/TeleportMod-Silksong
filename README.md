# Teleport Mod

> **English** | [中文](README_zh.md)

A simple teleportation mod for Hollow Knight: Silksong.

## English Documentation

<details>
<summary><h3>📋 Known Issues & Solutions</h3></summary>

#### 🔧 Camera Issues
If you find the character is off-screen after teleporting, or the camera doesn't follow character movement (character walks directly out of screen boundaries), this is usually caused by skipping the game's camera transition mechanism during teleportation. For solutions, please refer to the Safe Respawn function in the [Troubleshooting Guide](#troubleshooting).

#### ⚠️ Teleporting to Unlocked Entry Points  
Both teleportation and Safe Respawn functions may sometimes teleport the character to unlocked entry points. Due to potential obstacles at unlocked entrances, the character might be "pushed into" unexpected closed areas. If you find yourself trapped in a scene that you cannot normally exit, please:
- Use **Teleport to Bench** function to return to the last save point
- Or use **Emergency Teleport** to return to a safe location
- Temporarily avoid saving coordinates in such areas

📍 **Special Note**: The "MEMORIUM" scene (requires double jump to reach) is prone to this issue. If teleported there before unlocking double jump, you will be trapped in the scene, please be extra careful.

#### 🗺️ Map Key Hold Causing Incomplete Teleportation
During cross-scene teleportation, if the teleport key combination includes the map key (gamepad default LB), holding the map key may cause teleportation only to the scene entrance rather than the target coordinates. When this occurs:
- Release the map key
- Teleport to the same coordinate save again
- Or use the Safe Respawn function

💡 **Tip**: While this operation method is uncommon, both keyboard and gamepad can trigger this issue.

</details>

### Usage

#### Keyboard Controls
- `Ctrl + 1~5`: Quick save position 🔊 Sound effect
- `Alt + 1~5`: Quick load position (no save data: auto teleport to bench)
- `Alt + 6`: **Safe respawn** (cycle entry points)
- `Alt + 7`: **Teleport to bench** (last respawn point)
- `Alt + 9`: **Teleport to the fixed bench at the bottom of the Abyss**
- `Alt + 0`: **Reset all coordinates**
- `Alt + - [minus key]`: **Emergency teleport** (to preset safe location) 
- 🌟 ``Ctrl + ` [tilde key]``: **Open Teleport Interface** - Unlimited save slots with UI support for save, load, delete, overwrite, bench teleport, reset coordinates, safe respawn, and more
- **🆘 `Ctrl + F9`: Emergency return to main menu** (character out of control/stuck)

```
💡 Note: Default uses main keyboard number keys, NOT numpad keys. All key combinations are fully customizable in the config file, including modifier keys and function keys!
```

#### Gamepad Controls
- `LB + RB + Direction/A`: Teleport to slot
- `LB + Start + Direction/A`: Save to slot 🔊 Sound effect
- `LB + RB + Y`: **Safe respawn**
- `LB + RB + B`: **Teleport to bench** (last respawn point)
- `LB + Select + Start`: **Reset all coordinates**
- `LB + RB + X`: **Emergency teleport** (to preset safe location)

```
💡 Note: All gamepad controls are fully customizable to match your personal preferences.
```

#### 🎮 Gamepad Key Reference

**Direction Keys:**
- `DPadUp` = D-Pad Up
- `DPadDown` = D-Pad Down  
- `DPadLeft` = D-Pad Left
- `DPadRight` = D-Pad Right

**Face Buttons:**
- `JoystickButton0` = A Button
- `JoystickButton1` = B Button
- `JoystickButton2` = X Button
- `JoystickButton3` = Y Button

**Shoulders & Triggers:**
- `LeftBumper` = Left Bumper (LB)
- `RightBumper` = Right Bumper (RB)
- `LeftTrigger` = Left Trigger (LT)
- `RightTrigger` = Right Trigger (RT)

**System Buttons:**
- `JoystickButton6` = Select/Back Button
- `JoystickButton7` = Start Button
- `JoystickButton8` = Home/Guide Button

**Default Gamepad Configuration:**
- Slot 1: `DPadUp` (D-Pad Up)
- Slot 2: `DPadDown` (D-Pad Down)
- Slot 3: `DPadLeft` (D-Pad Left)
- Slot 4: `DPadRight` (D-Pad Right)
- Slot 5: `JoystickButton0` (A Button)
- Teleport Modifiers: `LeftBumper` + `RightBumper` (LB + RB)
- Save Modifiers: `LeftBumper` + `JoystickButton7` (LB + Start)
- Safe Respawn: `JoystickButton3` (Y Button) [in teleport mode]
- Hardcoded Teleport: `JoystickButton2` (X Button) [in teleport mode]
- Bench Teleport: `JoystickButton1` (B Button) [in teleport mode]
- Reset All: `LeftBumper` + `JoystickButton6` + `JoystickButton7` (LB + Select + Start)

```
⚙️ All gamepad controls can be customized in the game's config file.
💡 If older version config entries are too numerous and affect viewing, you can delete the .cfg config file and restart the game to automatically generate the latest configuration.
```

### Safety Guidelines

> **⚠️ Important: Please follow these safety guidelines to avoid game bugs and data corruption!**

#### When it's safe to use:
✅ Only save or teleport when your **character is fully controllable**  
✅ In **normal game scenes**, when **not in combat**

#### Dangerous situations - DO NOT use:
❌ **During boss battles**  
❌ **Inside closed combat areas**  
❌ **During cutscenes or animations**  
❌ **When character is controlled or immobilized**  
❌ **During any special states or triggered events**

#### Important Notes:
⚠️ **Avoid multiple teleportations in short time** (like teleporting multiple times within 1 second)  
⚠️ **Do not teleport immediately after death**

---

### Troubleshooting:

#### 🆘 If stuck/camera lost/character floating and unable to open menu:

1. **First try: Safe Respawn** (Keyboard: `Alt+6`, Gamepad: `LB+RB+Y`)  
   If teleported to entry point, then use load position as needed  
   > 💡 Example: If character is off-screen after loading save, or camera doesn't follow character movement, use `Alt+6` safe respawn, then reload the same save slot coordinates to fix camera/view issues

2. **Character completely out of control, or character floating and not landing: Emergency return to main menu** (Keyboard: `Ctrl+F9`, no gamepad shortcut)  
   > ⚠️ **Important**: **Do NOT use emergency return to main menu when character is dead**

3. **Still not working, completely unresponsive: Restart the game**

---

#### 🚨 All save data lost or stuck in a scene:

If all your **save slots are lost** or you're trapped in an unescapable scene, use **Emergency Teleport** (Keyboard: `Alt+-` [minus key], Gamepad: `LB+RB+X`) to instantly teleport to a **preset safe location** (starting town station).

> **This works independently of your save data**

---

### Emergency rescue config:

If all methods fail, you can **manually edit the save file** with this safe config:

> ⚠️ **Important:** **Close the game first**, modify and save, then restart the game, and load slot 1 after entering the game (Keyboard: `Alt+1`, Gamepad: `LB+RB+Up`)

```json
{
  "saveSlots": {
    "1": {
      "x": 71.42231,
      "y": 9.597684,
      "z": 0.004,
      "scene": "Bellway_01",
      "hasData": true
    }
  }
}
```

**Slot 1 defaults to the station in the starting town, which is absolutely safe.**

**Config file location:**  
`C:\Users\[Username]\AppData\LocalLow\Team Cherry\Hollow Knight Silksong\TeleportMod\savedata.json`

### Installation

1. Install BepInEx
2. Extract and put the `Teleport` related folder into `BepInEx/plugins/` folder
3. Ensure `Teleport.dll`, `manbo.wav`, and `Gamesave.wav` are in the same directory under the folder
4. Start game

### Config

- `Enable Detailed Logging` (default: `false`): Show detailed logs
- `Enable Gamepad Support` (default: `true`): Enable controller input
- `Enable Easter Egg Audio` (default: `false`): 🎵 Enable special sound effect for saving
- `Audio Volume` (default: `0.5`): 🔊 Volume level for save sound effect (0.0-1.0, 0=disable)
- `Save Modifier Key` (default: `LeftControl`): Modifier key for saving coordinates
- `Teleport Modifier Key` (default: `LeftAlt`): Modifier key for teleporting
- `Reset Modifier Key` (default: `LeftAlt`): Modifier key for reset functions
- `Slot 1-5 Keys` (default: `Alpha1-5`): Keys for save slots
- `Safe Respawn Key` (default: `Alpha6`): Key for safe respawn function
- `Bench Teleport Key` (default: `Alpha7`): Key for bench teleport function
- `Abyss Bench Teleport Key` (default: `Alpha9`): Key for the fixed Abyss bench (used with the teleport modifier)
- `Emergency Teleport Key` (default: `Minus`): Key for emergency teleport function
- `Reset All Key` (default: `Alpha0`): Key for reset all function

#### Data Storage
Coordinate data path:  
`C:\Users\[Username]\AppData\LocalLow\Team Cherry\Hollow Knight Silksong\TeleportMod\savedata.json`

Configuration file location:  
`{Game Install Directory}\BepInEx\config\Mhz.TeleportMod.cfg`

```
💡 Note: If the config file or folder doesn't exist, please run the game once to generate it first.
```
