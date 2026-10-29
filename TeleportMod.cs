using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;
using GlobalEnums;
using InControl;
using HarmonyLib;

[BepInPlugin("Mhz.TeleportMod", "Teleport Mod", "1.2.2")]
public class TeleportMod : BaseUnityPlugin
{
    public new static ManualLogSource? Logger;

    // 配置项
    public static ConfigEntry<bool>? enableDetailedLogging;
    private static ConfigEntry<bool>? enableGamepadSupport;
    private static ConfigEntry<bool>? enableEasterEggAudio;
    private static ConfigEntry<string>? saveModifierKey;
    private static ConfigEntry<string>? teleportModifierKey;
    private static ConfigEntry<string>? resetModifierKey;

    // 手柄存档槽按键配置
    private static ConfigEntry<string>? gamepadSlot1Key;
    private static ConfigEntry<string>? gamepadSlot2Key;
    private static ConfigEntry<string>? gamepadSlot3Key;
    private static ConfigEntry<string>? gamepadSlot4Key;
    private static ConfigEntry<string>? gamepadSlot5Key;

    // 手柄修饰键配置
    private static ConfigEntry<string>? gamepadTeleportModifier1;
    private static ConfigEntry<string>? gamepadTeleportModifier2;
    private static ConfigEntry<string>? gamepadSaveModifier;
    private static ConfigEntry<string>? gamepadSaveTrigger;

    // 手柄特殊功能按键配置
    private static ConfigEntry<string>? gamepadSafeRespawnKey;
    private static ConfigEntry<string>? gamepadHardcodedTeleportKey;
    private static ConfigEntry<string>? gamepadBenchTeleportKey;
    private static ConfigEntry<string>? gamepadClearAllModifier1;
    private static ConfigEntry<string>? gamepadClearAllModifier2;
    private static ConfigEntry<string>? gamepadClearAllTrigger;

    // 键盘存档槽按键配置（保持向后兼容）
    private static ConfigEntry<string>? slot1Key;
    private static ConfigEntry<string>? slot2Key;
    private static ConfigEntry<string>? slot3Key;
    private static ConfigEntry<string>? slot4Key;
    private static ConfigEntry<string>? slot5Key;

    // 键盘特殊功能按键配置（保持向后兼容）
    private static ConfigEntry<string>? safeRespawnKey;
    private static ConfigEntry<string>? resetAllKey;
    private static ConfigEntry<string>? hardcodedTeleportKey;
    private static ConfigEntry<string>? benchTeleportKey;
    private static ConfigEntry<string>? abyssBenchTeleportKey;

    // 音效设置
    private static ConfigEntry<float>? audioVolume;

    // 多档位存档系统
    private static Dictionary<int, SaveSlot> saveSlots = new Dictionary<int, SaveSlot>();

    // 扩展存档系统 - 支持无限存档
    private static Dictionary<string, ExtendedSaveSlot> extendedSaveSlots = new Dictionary<string, ExtendedSaveSlot>();

    // 全局序号计数器（持续递增，不重复使用）
    private static int nextSerialNumber = 1;

    // UI管理器
    private static TeleportUIManager? uiManager;

    // Alt+6功能的入口点轮换索引（同场景内轮换，切换场景时重置）
    private static int currentEntryPointIndex = 0;

    // 记录上次使用Alt+6的场景，用于检测场景切换并重置索引
    private static string lastUsedScene = "";

    // 手柄轴输入状态跟踪
    private static bool wasVerticalPressed = false;
    private static bool wasHorizontalPressed = false;


    // 音频播放器复用
    private static GameObject? audioPlayerObject = null;
    private static AudioSource? audioPlayerSource = null;

    // 音频缓存
    private static AudioClip? cachedSaveAudioClip = null;
    private static float lastSaveAudioTime = 0f;
    private const float AUDIO_COOLDOWN = 0.1f; // 音频播放冷却时间

    // 椅子传送安全偏移
    private static readonly Vector3 BENCH_SAFE_OFFSET = new Vector3(0, 2f, 0);

    // 存档数据结构
    public struct SaveSlot
    {
        public Vector3 position;
        public string scene;
        public bool hasData;
        public DateTime saveTime;

        public SaveSlot(Vector3 pos, string sceneName)
        {
            position = pos;
            scene = sceneName;
            hasData = true;
            saveTime = DateTime.Now;
        }

        public SaveSlot(Vector3 pos, string sceneName, DateTime time)
        {
            position = pos;
            scene = sceneName;
            hasData = true;
            saveTime = time;
        }
    }

    // 扩展存档数据结构 - 支持更多信息
    public class ExtendedSaveSlot
    {
        public Vector3 position;
        public string scene = "";
        public DateTime saveTime;
        public string displayName = "";
        public bool hasData = false;
        public string customNote = "";
        public int serialNumber = 0;  // 序号（用于UI显示排序）

        public ExtendedSaveSlot() { }

        public ExtendedSaveSlot(Vector3 pos, string sceneName, string? name = null, string? note = null, bool assignNewSerial = true)
        {
            position = pos;
            scene = sceneName ?? "";
            saveTime = DateTime.Now;
            // 使用场景名+时间戳作为默认名称（只显示时间，不显示日期）
            displayName = name ?? $"{sceneName} {saveTime:HH:mm}";
            customNote = note ?? "";
            hasData = true;
            // 只在需要时分配新序号（新建存档），覆盖时不分配
            serialNumber = assignNewSerial ? nextSerialNumber++ : 0;
        }

        public ExtendedSaveSlot(SaveSlot oldSlot)
        {
            position = oldSlot.position;
            scene = oldSlot.scene ?? "";
            saveTime = oldSlot.saveTime;  // 保持原始时间戳，不重新设置
            // 传统存档使用简单的Legacy标识，具体名称在GetAllSaveSlots中设置
            displayName = $"Legacy Save {saveTime:HH:mm}";
            customNote = "";
            hasData = oldSlot.hasData;
        }
    }

    // 可序列化的存档数据
    [System.Serializable]
    public class PersistentData
    {
        public Dictionary<int, SerializableSaveSlot> saveSlots = new Dictionary<int, SerializableSaveSlot>();
        public Dictionary<string, SerializableExtendedSaveSlot> extendedSlots = new Dictionary<string, SerializableExtendedSaveSlot>();
        public int nextSerialNumber = 1;  // 序号计数器
    }

    [System.Serializable]
    public class SerializableSaveSlot
    {
        public float x, y, z;
        public string scene = "";
        public bool hasData = false;
        public string saveTimeString = "";

        public SerializableSaveSlot() { }

        public SerializableSaveSlot(SaveSlot slot)
        {
            x = slot.position.x;
            y = slot.position.y;
            z = slot.position.z;
            scene = slot.scene ?? "";
            hasData = slot.hasData;
            saveTimeString = slot.saveTime.ToString("yyyy-MM-dd HH:mm:ss");
        }

        public SaveSlot ToSaveSlot()
        {
            // 解析保存时间
            DateTime saveTime;
            if (DateTime.TryParse(saveTimeString, out saveTime))
            {
                return new SaveSlot(new Vector3(x, y, z), scene, saveTime);
            }
            else
            {
                // 如果解析失败，使用默认时间
                return new SaveSlot(new Vector3(x, y, z), scene);
            }
        }
    }

    [System.Serializable]
    public class SerializableExtendedSaveSlot
    {
        public float x, y, z;
        public string scene = "";
        public string saveTimeString = "";
        public string displayName = "";
        public bool hasData = false;
        public string customNote = "";
        public int serialNumber = 0;  // 序号

        public SerializableExtendedSaveSlot() { }

        public SerializableExtendedSaveSlot(ExtendedSaveSlot slot)
        {
            x = slot.position.x;
            y = slot.position.y;
            z = slot.position.z;
            scene = slot.scene ?? "";
            saveTimeString = slot.saveTime.ToString("yyyy-MM-dd HH:mm:ss");
            displayName = slot.displayName ?? "";
            customNote = slot.customNote ?? "";
            hasData = slot.hasData;
            serialNumber = slot.serialNumber;
        }

        public ExtendedSaveSlot ToExtendedSaveSlot()
        {
            var slot = new ExtendedSaveSlot();
            slot.position = new Vector3(x, y, z);
            slot.scene = scene ?? "";
            slot.displayName = displayName ?? "";
            slot.customNote = customNote ?? "";
            slot.hasData = hasData;
            slot.serialNumber = serialNumber;

            // 解析保存时间
            if (DateTime.TryParse(saveTimeString, out DateTime parsedTime))
            {
                slot.saveTime = parsedTime;
            }
            else
            {
                slot.saveTime = DateTime.Now;
            }

            return slot;
        }
    }

    private void Awake()
    {
        Logger = base.Logger;

        // 初始化Harmony补丁
        try
        {
            var harmony = new Harmony("Mhz.TeleportMod");
            harmony.PatchAll();
            Logger?.LogInfo("Harmony补丁已应用");
        }
        catch (Exception ex)
        {
            Logger?.LogError($"初始化Harmony时发生错误: {ex.Message}");
        }

        // 初始化配置项
        enableDetailedLogging = Config.Bind("日志设置 | Logging", "启用详细日志 | Enable Detailed Logging", false, "是否启用详细的传送日志输出 | Enable detailed teleport logging output");
        enableGamepadSupport = Config.Bind("控制设置 | Controls", "启用手柄支持 | Enable Gamepad Support", true,
            "是否启用手柄控制传送功能。📖 完整操作方案请查看 README.md | " +
            "Enable gamepad control for teleport functions. 📖 For complete operation guide, please check README.md");

        // 音效设置
        enableEasterEggAudio = Config.Bind("音效设置 | Audio Settings", "启用彩蛋音效 | Enable Easter Egg Audio", false,
            "是否启用彩蛋音效。开启后存档时播放特殊音效，关闭时播放默认音效。需要重启游戏生效 | Enable easter egg audio effect. When enabled, plays special sound effect when saving, otherwise plays default sound effect. Requires game restart to take effect");

        audioVolume = Config.Bind("音效设置 | Audio Settings", "音效音量 | Audio Volume", 0.5f,
            "存档音效的音量大小。范围0.0-1.0，设置为0关闭音效 | Volume level for save sound effect. Range 0.0-1.0, set to 0 to disable audio");

        // 按键设置
        saveModifierKey = Config.Bind("按键设置 | Key Settings", "保存修饰键 | Save Modifier Key", "LeftControl",
            "保存坐标时使用的修饰键。可选值：LeftControl, RightControl, LeftAlt, RightAlt, LeftShift, RightShift | " +
            "Modifier key for saving coordinates. Options: LeftControl, RightControl, LeftAlt, RightAlt, LeftShift, RightShift");

        teleportModifierKey = Config.Bind("按键设置 | Key Settings", "传送修饰键 | Teleport Modifier Key", "LeftAlt",
            "传送坐标时使用的修饰键。可选值：LeftControl, RightControl, LeftAlt, RightAlt, LeftShift, RightShift | " +
            "Modifier key for teleporting coordinates. Options: LeftControl, RightControl, LeftAlt, RightAlt, LeftShift, RightShift");

        resetModifierKey = Config.Bind("按键设置 | Key Settings", "重置修饰键 | Reset Modifier Key", "LeftAlt",
            "重置坐标和安全重生时使用的修饰键。可选值：LeftControl, RightControl, LeftAlt, RightAlt, LeftShift, RightShift | " +
            "Modifier key for reset and safe respawn functions. Options: LeftControl, RightControl, LeftAlt, RightAlt, LeftShift, RightShift");

        // 手柄存档槽按键配置
        gamepadSlot1Key = Config.Bind("手柄存档槽按键 | Gamepad Slot Keys", "手柄存档槽1 | Gamepad Slot 1", "DPadUp",
            "手柄存档槽1按键 | Gamepad key for slot 1 (默认: DPadUp=方向键上 | Default: DPadUp=D-Pad Up). 📖 完整按键对照表请查看 README.md | For complete key reference, see README.md");
        gamepadSlot2Key = Config.Bind("手柄存档槽按键 | Gamepad Slot Keys", "手柄存档槽2 | Gamepad Slot 2", "DPadDown",
            "手柄存档槽2按键 | Gamepad key for slot 2 (默认: DPadDown=方向键下 | Default: DPadDown=D-Pad Down)");
        gamepadSlot3Key = Config.Bind("手柄存档槽按键 | Gamepad Slot Keys", "手柄存档槽3 | Gamepad Slot 3", "DPadLeft",
            "手柄存档槽3按键 | Gamepad key for slot 3 (默认: DPadLeft=方向键左 | Default: DPadLeft=D-Pad Left)");
        gamepadSlot4Key = Config.Bind("手柄存档槽按键 | Gamepad Slot Keys", "手柄存档槽4 | Gamepad Slot 4", "DPadRight",
            "手柄存档槽4按键 | Gamepad key for slot 4 (默认: DPadRight=方向键右 | Default: DPadRight=D-Pad Right)");
        gamepadSlot5Key = Config.Bind("手柄存档槽按键 | Gamepad Slot Keys", "手柄存档槽5 | Gamepad Slot 5", "JoystickButton0",
            "手柄存档槽5按键 | Gamepad key for slot 5 (默认: JoystickButton0=A按钮 | Default: JoystickButton0=A Button)");

        // 手柄修饰键配置
        gamepadTeleportModifier1 = Config.Bind("手柄修饰键 | Gamepad Modifiers", "传送修饰键1 | Teleport Modifier 1", "LeftBumper",
            "传送修饰键1 (默认: LeftBumper=LB) | Teleport modifier 1 (Default: LeftBumper=LB). 组合: 修饰键1 + 修饰键2 + 存档槽 = 传送 | Combo: Modifier1 + Modifier2 + Slot = Teleport");
        gamepadTeleportModifier2 = Config.Bind("手柄修饰键 | Gamepad Modifiers", "传送修饰键2 | Teleport Modifier 2", "RightBumper",
            "传送修饰键2 (默认: RightBumper=RB) | Teleport modifier 2 (Default: RightBumper=RB). 默认组合: LB + RB | Default combo: LB + RB");
        gamepadSaveModifier = Config.Bind("手柄修饰键 | Gamepad Modifiers", "保存修饰键 | Save Modifier", "LeftBumper",
            "保存修饰键 (默认: LeftBumper=LB) | Save modifier (Default: LeftBumper=LB). 组合: 保存修饰键 + 保存触发键 + 存档槽 = 保存 | Combo: Save Modifier + Save Trigger + Slot = Save");
        gamepadSaveTrigger = Config.Bind("手柄修饰键 | Gamepad Modifiers", "保存触发键 | Save Trigger", "JoystickButton7",
            "保存触发键 (默认: JoystickButton7=Start) | Save trigger (Default: JoystickButton7=Start). 默认组合: LB + Start | Default combo: LB + Start");

        // 手柄特殊功能按键配置  
        gamepadSafeRespawnKey = Config.Bind("手柄特殊功能 | Gamepad Special", "安全重生按键 | Safe Respawn", "JoystickButton3",
            "安全重生按键 (默认: JoystickButton3=Y按钮) | Safe respawn key (Default: JoystickButton3=Y Button). 传送模式下使用 | Use in teleport mode");
        gamepadHardcodedTeleportKey = Config.Bind("手柄特殊功能 | Gamepad Special", "硬编码传送按键 | Hardcoded Teleport", "JoystickButton2",
            "硬编码传送按键 (默认: JoystickButton2=X按钮) | Hardcoded teleport key (Default: JoystickButton2=X Button). 传送模式下使用 | Use in teleport mode");
        gamepadBenchTeleportKey = Config.Bind("手柄特殊功能 | Gamepad Special", "椅子传送按键 | Bench Teleport", "JoystickButton1",
            "椅子传送按键 (默认: JoystickButton1=B按钮) | Bench teleport key (Default: JoystickButton1=B Button). 传送模式下使用 | Use in teleport mode");

        // 手柄重置组合键配置
        gamepadClearAllModifier1 = Config.Bind("手柄重置组合 | Gamepad Reset Combo", "重置修饰键1 | Reset Modifier 1", "LeftBumper",
            "重置修饰键1 (默认: LeftBumper=LB) | Reset modifier 1 (Default: LeftBumper=LB). 三键组合清空所有存档 | Triple key combo to clear all saves");
        gamepadClearAllModifier2 = Config.Bind("手柄重置组合 | Gamepad Reset Combo", "重置修饰键2 | Reset Modifier 2", "JoystickButton6",
            "重置修饰键2 (默认: JoystickButton6=Select) | Reset modifier 2 (Default: JoystickButton6=Select). 默认组合: LB + Select + Start | Default combo: LB + Select + Start");
        gamepadClearAllTrigger = Config.Bind("手柄重置组合 | Gamepad Reset Combo", "重置触发键 | Reset Trigger", "JoystickButton7",
            "重置触发键 (默认: JoystickButton7=Start) | Reset trigger (Default: JoystickButton7=Start). ⚠️警告: 此操作不可撤销 | ⚠️Warning: This action is irreversible");

        // 键盘存档槽按键配置（保持向后兼容）
        slot1Key = Config.Bind("键盘存档槽按键 | Keyboard Slot Keys", "键盘存档槽1 | Keyboard Slot 1", "Alpha1",
            "键盘存档槽1按键。可用：Alpha0-9, F1-F12, Q, W, E, R, T, Y, U, I, O, P等 | Keyboard key for slot 1. Available: Alpha0-9, F1-F12, Q, W, E, R, T, Y, U, I, O, P, etc.");
        slot2Key = Config.Bind("键盘存档槽按键 | Keyboard Slot Keys", "键盘存档槽2 | Keyboard Slot 2", "Alpha2",
            "键盘存档槽2按键 | Keyboard key for slot 2");
        slot3Key = Config.Bind("键盘存档槽按键 | Keyboard Slot Keys", "键盘存档槽3 | Keyboard Slot 3", "Alpha3",
            "键盘存档槽3按键 | Keyboard key for slot 3");
        slot4Key = Config.Bind("键盘存档槽按键 | Keyboard Slot Keys", "键盘存档槽4 | Keyboard Slot 4", "Alpha4",
            "键盘存档槽4按键 | Keyboard key for slot 4");
        slot5Key = Config.Bind("键盘存档槽按键 | Keyboard Slot Keys", "键盘存档槽5 | Keyboard Slot 5", "Alpha5",
            "键盘存档槽5按键 | Keyboard key for slot 5");

        // 键盘特殊功能按键配置（保持向后兼容）
        safeRespawnKey = Config.Bind("键盘特殊功能 | Keyboard Special", "键盘安全重生 | Keyboard Safe Respawn", "Alpha6",
            "键盘安全重生功能按键 | Keyboard safe respawn function key");
        resetAllKey = Config.Bind("键盘特殊功能 | Keyboard Special", "键盘重置所有 | Keyboard Reset All", "Alpha0",
            "键盘重置所有坐标功能按键 | Keyboard reset all coordinates function key");
        hardcodedTeleportKey = Config.Bind("键盘特殊功能 | Keyboard Special", "键盘硬编码传送 | Keyboard Hardcoded Teleport", "Minus",
            "键盘传送到预设坐标的按键。默认是减号键(-) | Keyboard key for teleporting to preset coordinates. Default is minus key (-)");
        benchTeleportKey = Config.Bind("键盘特殊功能 | Keyboard Special", "键盘椅子传送 | Keyboard Bench Teleport", "Alpha7",
            "键盘传送到椅子（最后重生点）的按键 | Keyboard key for teleporting to bench (last respawn point)");
        abyssBenchTeleportKey = Config.Bind("键盘特殊功能 | Keyboard Special", "键盘深渊椅子传送 | Keyboard Abyss Bench Teleport", "Alpha9",
            "键盘传送到深渊固定椅子的按键 | Keyboard key for teleporting to the fixed Abyss bench");

        Logger?.LogInfo("Teleport Mod 已加载!");

        if (enableDetailedLogging?.Value == true)
        {
            Logger?.LogInfo("详细日志已启用 | Detailed logging enabled");
        }
        else
        {
            Logger?.LogInfo("详细日志已禁用，只显示重要信息 | Detailed logging disabled, showing important messages only");
        }

        if (enableGamepadSupport?.Value == true)
        {
            Logger?.LogInfo("手柄支持已启用 | Gamepad support enabled");
        }
        else
        {
            Logger?.LogInfo("手柄支持已禁用 | Gamepad support disabled");
        }

        // 加载持久化数据
        LoadPersistentData();

        // 预加载音频文件
        StartCoroutine(PreloadAudioClip());

        // 初始化UI管理器
        InitializeUIManager();
    }

    // 加载持久化数据
    private void LoadPersistentData()
    {
        try
        {
            string filePath = GetSaveFilePath();
            if (File.Exists(filePath))
            {
                string json = File.ReadAllText(filePath);
                PersistentData? data = JsonConvert.DeserializeObject<PersistentData>(json);

                if (data != null)
                {
                    // 恢复传统存档槽数据
                    if (data.saveSlots != null)
                    {
                        saveSlots.Clear();
                        foreach (var kvp in data.saveSlots)
                        {
                            if (kvp.Value != null && kvp.Value.hasData)
                            {
                                saveSlots[kvp.Key] = kvp.Value.ToSaveSlot();
                            }
                        }
                    }

                    // 恢复扩展存档数据
                    if (data.extendedSlots != null)
                    {
                        extendedSaveSlots.Clear();
                        foreach (var kvp in data.extendedSlots)
                        {
                            if (kvp.Value != null && kvp.Value.hasData)
                            {
                                var slot = kvp.Value.ToExtendedSaveSlot();
                                // 对于旧数据（没有序号），分配新序号
                                if (slot.serialNumber <= 0)
                                {
                                    slot.serialNumber = nextSerialNumber++;
                                }
                                extendedSaveSlots[kvp.Key] = slot;
                            }
                        }
                    }

                    // 恢复序号计数器，确保不会重复分配
                    nextSerialNumber = Math.Max(data.nextSerialNumber, nextSerialNumber);

                    int totalSlots = (data.saveSlots?.Count ?? 0) + (data.extendedSlots?.Count ?? 0);
                    Logger?.LogInfo($"已加载持久化数据：{data.saveSlots?.Count ?? 0} 个传统存档，{data.extendedSlots?.Count ?? 0} 个扩展存档，下一个序号: {nextSerialNumber} | Loaded persistent data: {data.saveSlots?.Count ?? 0} traditional slots, {data.extendedSlots?.Count ?? 0} extended slots, next serial: {nextSerialNumber}");
                }
            }
            else
            {
                LogInfo("未找到存档文件，使用默认设置 | No save file found, using defaults");
            }
        }
        catch (Exception ex)
        {
            Logger?.LogError($"加载持久化数据时发生错误 | Error loading persistent data: {ex.Message}");
        }
    }

    // 保存持久化数据
    private void SavePersistentData()
    {
        try
        {
            PersistentData data = new PersistentData();

            // 保存传统存档槽数据
            foreach (var kvp in saveSlots)
            {
                if (kvp.Value.hasData)
                {
                    data.saveSlots[kvp.Key] = new SerializableSaveSlot(kvp.Value);
                }
            }

            // 保存扩展存档数据
            foreach (var kvp in extendedSaveSlots)
            {
                if (kvp.Value.hasData)
                {
                    data.extendedSlots[kvp.Key] = new SerializableExtendedSaveSlot(kvp.Value);
                }
            }

            // 保存序号计数器
            data.nextSerialNumber = nextSerialNumber;

            string json = JsonConvert.SerializeObject(data, Formatting.Indented);
            string filePath = GetSaveFilePath();

            // 确保目录存在
            string directory = Path.GetDirectoryName(filePath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(filePath, json);
            int totalSlots = data.saveSlots.Count + data.extendedSlots.Count;
            LogInfo($"已保存持久化数据：{data.saveSlots.Count} 个传统存档，{data.extendedSlots.Count} 个扩展存档 | Saved persistent data: {data.saveSlots.Count} traditional slots, {data.extendedSlots.Count} extended slots");
        }
        catch (Exception ex)
        {
            Logger?.LogError($"保存持久化数据时发生错误 | Error saving persistent data: {ex.Message}");
        }
    }

    // 获取存档文件路径
    private string GetSaveFilePath()
    {
        try
        {
            // 使用Unity的persistentDataPath获取游戏数据目录
            // 通常是: C:\Users\[用户名]\AppData\LocalLow\Team Cherry\Hollow Knight Silksong
            string gameDataPath = Application.persistentDataPath;

            // 在游戏数据目录下创建TeleportMod子文件夹
            string modDataPath = Path.Combine(gameDataPath, "TeleportMod");

            // 返回完整的JSON文件路径
            string saveFilePath = Path.Combine(modDataPath, "savedata.json");

            LogInfo($"存档文件路径 | Save file path: {saveFilePath}");
            return saveFilePath;
        }
        catch (Exception ex)
        {
            Logger?.LogError($"获取存档文件路径时发生错误 | Error getting save file path: {ex.Message}");
            // 出错时回退到相对路径
            return Path.Combine("TeleportMod", "savedata.json");
        }
    }

    // 辅助方法：只在配置启用时输出详细日志
    public static void LogInfo(string message)
    {
        if (enableDetailedLogging?.Value == true)
        {
            Logger?.LogInfo(message);
        }
    }

    // 为椅子位置添加安全偏移，防止卡地里
    private static Vector3 ApplyBenchSafeOffset(Vector3 benchPosition)
    {
        // 不对占位符坐标添加偏移
        if (benchPosition == Vector3.one || benchPosition == Vector3.zero)
        {
            return benchPosition;
        }

        Vector3 safePosition = benchPosition + BENCH_SAFE_OFFSET;
        LogInfo($"椅子位置已添加安全偏移: {benchPosition} -> {safePosition}");
        return safePosition;
    }

    // 检查是否允许保存和传送操作
    public static bool CanPerformTeleportOperations()
    {
        try
        {
            // 检查血量是否为0（死亡）
            if (PlayerData.instance != null && PlayerData.instance.health <= 0)
            {
                LogInfo("角色血量为0，禁止保存和传送 | Hero health is 0, blocking save and teleport");
                return false;
            }

            // 检查是否在椅子上
            if (PlayerData.instance != null && PlayerData.instance.atBench)
            {
                LogInfo("角色在椅子上，禁止保存和传送 | Hero is at bench, blocking save and teleport");
                return false;
            }

            // 检查是否在特殊场景中
            if (PlayerData.instance != null && !PlayerData.instance.bindCutscenePlayed)
            {
                LogInfo("角色在特殊场景中，禁止保存和传送 | Teleport blocked");
                return false;
            }

            // 检查是否正在重生
            if (GameManager.instance != null && GameManager.instance.RespawningHero)
            {
                LogInfo("角色正在重生，禁止保存和传送 | Hero is respawning, blocking save and teleport");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            Logger?.LogError($"检查传送操作权限时发生错误: {ex.Message}");
            return false; // 出错时为安全起见禁止操作
        }
    }


    // 统一的手柄按键检测方法
    private static bool IsGamepadKeyPressed(string keyConfig)
    {
        if (string.IsNullOrEmpty(keyConfig)) return false;

        var device = InputManager.ActiveDevice;
        if (device == null || !device.IsAttached) return false;

        return keyConfig switch
        {
            // 方向键 (DPad)
            "DPadUp" => device.DPadUp.WasPressed,
            "DPadDown" => device.DPadDown.WasPressed,
            "DPadLeft" => device.DPadLeft.WasPressed,
            "DPadRight" => device.DPadRight.WasPressed,

            // 肩键 (Bumpers)
            "LeftBumper" => device.LeftBumper.WasPressed,
            "RightBumper" => device.RightBumper.WasPressed,

            // 扳机 (Triggers)
            "LeftTrigger" => device.LeftTrigger.WasPressed,
            "RightTrigger" => device.RightTrigger.WasPressed,

            // 摇杆按钮
            "LeftStickButton" => device.LeftStickButton.WasPressed,
            "RightStickButton" => device.RightStickButton.WasPressed,

            // 左摇杆方向
            "LeftStickUp" => device.LeftStickUp.WasPressed,
            "LeftStickDown" => device.LeftStickDown.WasPressed,
            "LeftStickLeft" => device.LeftStickLeft.WasPressed,
            "LeftStickRight" => device.LeftStickRight.WasPressed,

            // 右摇杆方向
            "RightStickUp" => device.RightStickUp.WasPressed,
            "RightStickDown" => device.RightStickDown.WasPressed,
            "RightStickLeft" => device.RightStickLeft.WasPressed,
            "RightStickRight" => device.RightStickRight.WasPressed,

            // 数字按钮 (JoystickButton0-19)
            _ when keyConfig.StartsWith("JoystickButton") => ParseJoystickButton(keyConfig),

            _ => false
        };
    }

    // 统一的手柄按键按住检测方法
    private static bool IsGamepadKeyHeld(string keyConfig)
    {
        if (string.IsNullOrEmpty(keyConfig)) return false;

        var device = InputManager.ActiveDevice;
        if (device == null || !device.IsAttached) return false;

        return keyConfig switch
        {
            // 方向键 (DPad)
            "DPadUp" => device.DPadUp.IsPressed,
            "DPadDown" => device.DPadDown.IsPressed,
            "DPadLeft" => device.DPadLeft.IsPressed,
            "DPadRight" => device.DPadRight.IsPressed,

            // 肩键 (Bumpers)
            "LeftBumper" => device.LeftBumper.IsPressed,
            "RightBumper" => device.RightBumper.IsPressed,

            // 扳机 (Triggers)
            "LeftTrigger" => device.LeftTrigger.IsPressed,
            "RightTrigger" => device.RightTrigger.IsPressed,

            // 摇杆按钮
            "LeftStickButton" => device.LeftStickButton.IsPressed,
            "RightStickButton" => device.RightStickButton.IsPressed,

            // 左摇杆方向
            "LeftStickUp" => device.LeftStickUp.IsPressed,
            "LeftStickDown" => device.LeftStickDown.IsPressed,
            "LeftStickLeft" => device.LeftStickLeft.IsPressed,
            "LeftStickRight" => device.LeftStickRight.IsPressed,

            // 右摇杆方向
            "RightStickUp" => device.RightStickUp.IsPressed,
            "RightStickDown" => device.RightStickDown.IsPressed,
            "RightStickLeft" => device.RightStickLeft.IsPressed,
            "RightStickRight" => device.RightStickRight.IsPressed,

            // 数字按钮 (JoystickButton0-19)
            _ when keyConfig.StartsWith("JoystickButton") => ParseJoystickButtonHeld(keyConfig),

            _ => false
        };
    }

    // 解析JoystickButton按键 (WasPressed)
    private static bool ParseJoystickButton(string keyConfig)
    {
        try
        {
            if (keyConfig.StartsWith("JoystickButton") && keyConfig.Length > 14)
            {
                string numberPart = keyConfig.Substring(14);
                if (int.TryParse(numberPart, out int buttonNumber) && buttonNumber >= 0 && buttonNumber <= 19)
                {
                    KeyCode keyCode = (KeyCode)System.Enum.Parse(typeof(KeyCode), keyConfig);
                    return Input.GetKeyDown(keyCode);
                }
            }
        }
        catch (Exception ex)
        {
            Logger?.LogError($"解析JoystickButton时发生错误: {keyConfig}, {ex.Message}");
        }
        return false;
    }

    // 解析JoystickButton按键 (IsPressed)
    private static bool ParseJoystickButtonHeld(string keyConfig)
    {
        try
        {
            if (keyConfig.StartsWith("JoystickButton") && keyConfig.Length > 14)
            {
                string numberPart = keyConfig.Substring(14);
                if (int.TryParse(numberPart, out int buttonNumber) && buttonNumber >= 0 && buttonNumber <= 19)
                {
                    KeyCode keyCode = (KeyCode)System.Enum.Parse(typeof(KeyCode), keyConfig);
                    return Input.GetKey(keyCode);
                }
            }
        }
        catch (Exception ex)
        {
            Logger?.LogError($"解析JoystickButton时发生错误: {keyConfig}, {ex.Message}");
        }
        return false;
    }

    private void Update()
    {
        // 使用UnsafeInstance避免游戏启动时的错误日志
        if (GameManager.UnsafeInstance == null)
        {
            return;
        }

        // 手柄输入检测
        if (enableGamepadSupport?.Value == true)
        {
            HandleGamepadInput();
        }

        // 键盘输入检测
        HandleKeyboardInput();
    }

    private void HandleKeyboardInput()
    {
        // 优先处理紧急重启按键，即使游戏暂停或状态异常也要响应
        HandleEmergencyRestartInput();

        // 检查游戏是否暂停，如果暂停则忽略其他输入
        var gm = GameManager.UnsafeInstance;
        if (gm == null || gm.isPaused || gm.GameState != GlobalEnums.GameState.PLAYING)
        {
            return;
        }

        // 检查是否允许传送操作（血量、椅子、重生状态）
        if (!CanPerformTeleportOperations())
        {
            return;
        }


        // 保存修饰键+存档槽按键 保存对应档位
        if (IsModifierKeyPressed(saveModifierKey?.Value ?? "LeftControl"))
        {
            for (int i = 1; i <= 5; i++)
            {
                KeyCode slotKey = GetSlotKey(i);
                if (slotKey != KeyCode.None && Input.GetKeyDown(slotKey))
                {
                    SaveToSlot(i);
                    break;
                }
            }
        }
        // 传送修饰键+存档槽按键 读取对应档位
        else if (IsModifierKeyPressed(teleportModifierKey?.Value ?? "LeftAlt"))
        {
            for (int i = 1; i <= 5; i++)
            {
                KeyCode slotKey = GetSlotKey(i);
                if (slotKey != KeyCode.None && Input.GetKeyDown(slotKey))
                {
                    LoadFromSlot(i);
                    break;
                }
            }
        }

        // 重置修饰键+特殊功能键
        if (IsModifierKeyPressed(resetModifierKey?.Value ?? "LeftAlt"))
        {
            // 重置修饰键+安全重生按键
            KeyCode safeRespawnKeyCode = ParseKeyCode(safeRespawnKey?.Value ?? "Alpha6");
            if (safeRespawnKeyCode != KeyCode.None && Input.GetKeyDown(safeRespawnKeyCode))
            {
                RespawnToSafeEntryPoint();
                return;
            }

            // 重置修饰键+重置所有坐标按键
            KeyCode resetAllKeyCode = ParseKeyCode(resetAllKey?.Value ?? "Alpha0");
            if (resetAllKeyCode != KeyCode.None && Input.GetKeyDown(resetAllKeyCode))
            {
                ClearAllSaveSlots();
                return;
            }

            // 重置修饰键+硬编码传送按键
            KeyCode hardcodedTeleportKeyCode = ParseKeyCode(hardcodedTeleportKey?.Value ?? "Minus");
            if (hardcodedTeleportKeyCode != KeyCode.None && Input.GetKeyDown(hardcodedTeleportKeyCode))
            {
                TeleportToHardcodedPosition();
                return;
            }
        }

        // 传送修饰键+椅子传送按键
        if (IsModifierKeyPressed(teleportModifierKey?.Value ?? "LeftAlt"))
        {
            KeyCode benchTeleportKeyCode = ParseKeyCode(benchTeleportKey?.Value ?? "Alpha7");
            if (benchTeleportKeyCode != KeyCode.None && Input.GetKeyDown(benchTeleportKeyCode))
            {
                TeleportToBench();
                return;
            }

            KeyCode abyssBenchTeleportKeyCode = ParseKeyCode(abyssBenchTeleportKey?.Value ?? "Alpha9");
            if (abyssBenchTeleportKeyCode != KeyCode.None && Input.GetKeyDown(abyssBenchTeleportKeyCode))
            {
                TeleportToAbyssBench();
                return;
            }
        }
    }

    private void HandleGamepadInput()
    {
        try
        {
            // 检查游戏是否暂停，如果暂停则忽略所有输入
            var gm = GameManager.UnsafeInstance;
            if (gm == null || gm.isPaused || gm.GameState != GlobalEnums.GameState.PLAYING)
            {
                return;
            }

            // 检查是否允许传送操作（血量、椅子、重生状态）
            if (!CanPerformTeleportOperations())
            {
                return;
            }


            // 使用自定义配置检查传送模式和保存模式
            bool teleportModeActive = IsGamepadKeyHeld(gamepadTeleportModifier1?.Value ?? "LeftBumper") &&
                                     IsGamepadKeyHeld(gamepadTeleportModifier2?.Value ?? "RightBumper");

            bool saveModeActive = IsGamepadKeyHeld(gamepadSaveModifier?.Value ?? "LeftBumper") &&
                                 IsGamepadKeyHeld(gamepadSaveTrigger?.Value ?? "JoystickButton7");

            // 如果既不是传送模式也不是保存模式，直接返回
            if (!teleportModeActive && !saveModeActive) return;

            // 安全重生按键（仅在传送模式下）
            if (teleportModeActive && IsGamepadKeyPressed(gamepadSafeRespawnKey?.Value ?? "JoystickButton3"))
            {
                RespawnToSafeEntryPoint();
                return;
            }

            // 硬编码传送按键（仅在传送模式下）
            if (teleportModeActive && IsGamepadKeyPressed(gamepadHardcodedTeleportKey?.Value ?? "JoystickButton2"))
            {
                TeleportToHardcodedPosition();
                return;
            }

            // 椅子传送按键（仅在传送模式下）
            if (teleportModeActive && IsGamepadKeyPressed(gamepadBenchTeleportKey?.Value ?? "JoystickButton1"))
            {
                TeleportToBench();
                return;
            }

            // 清空所有存档坐标组合键
            bool clearAllActive = IsGamepadKeyHeld(gamepadClearAllModifier1?.Value ?? "LeftBumper") &&
                                  IsGamepadKeyHeld(gamepadClearAllModifier2?.Value ?? "JoystickButton6");

            if (clearAllActive && IsGamepadKeyPressed(gamepadClearAllTrigger?.Value ?? "JoystickButton7"))
            {
                ClearAllSaveSlots();
                return;
            }

            // 存档槽按键检测（使用自定义配置）
            int slotNumber = 0;

            if (!wasVerticalPressed || !wasHorizontalPressed)
            {
                // 检测存档槽1-5
                if (!wasVerticalPressed && IsGamepadKeyPressed(gamepadSlot1Key?.Value ?? "DPadUp"))
                {
                    slotNumber = 1;
                    wasVerticalPressed = true;
                    LogInfo($"检测到存档槽1按键: {gamepadSlot1Key?.Value}");
                }
                else if (!wasVerticalPressed && IsGamepadKeyPressed(gamepadSlot2Key?.Value ?? "DPadDown"))
                {
                    slotNumber = 2;
                    wasVerticalPressed = true;
                    LogInfo($"检测到存档槽2按键: {gamepadSlot2Key?.Value}");
                }
                else if (!wasHorizontalPressed && IsGamepadKeyPressed(gamepadSlot3Key?.Value ?? "DPadLeft"))
                {
                    slotNumber = 3;
                    wasHorizontalPressed = true;
                    LogInfo($"检测到存档槽3按键: {gamepadSlot3Key?.Value}");
                }
                else if (!wasHorizontalPressed && IsGamepadKeyPressed(gamepadSlot4Key?.Value ?? "DPadRight"))
                {
                    slotNumber = 4;
                    wasHorizontalPressed = true;
                    LogInfo($"检测到存档槽4按键: {gamepadSlot4Key?.Value}");
                }
                else if (IsGamepadKeyPressed(gamepadSlot5Key?.Value ?? "JoystickButton0"))
                {
                    slotNumber = 5;
                    LogInfo($"检测到存档槽5按键: {gamepadSlot5Key?.Value}");
                }
            }

            // 重置按下状态（根据配置检测）
            var slot1Key = gamepadSlot1Key?.Value ?? "DPadUp";
            var slot2Key = gamepadSlot2Key?.Value ?? "DPadDown";
            var slot3Key = gamepadSlot3Key?.Value ?? "DPadLeft";
            var slot4Key = gamepadSlot4Key?.Value ?? "DPadRight";

            if (wasVerticalPressed && !IsGamepadKeyHeld(slot1Key) && !IsGamepadKeyHeld(slot2Key))
                wasVerticalPressed = false;
            if (wasHorizontalPressed && !IsGamepadKeyHeld(slot3Key) && !IsGamepadKeyHeld(slot4Key))
                wasHorizontalPressed = false;

            // 执行档位操作
            if (slotNumber > 0)
            {
                if (saveModeActive)
                {
                    SaveToSlot(slotNumber);
                }
                else if (teleportModeActive)
                {
                    LoadFromSlot(slotNumber);
                }
            }
        }
        catch (Exception ex)
        {
            Logger?.LogError($"处理手柄输入时发生错误 | Error handling gamepad input: {ex.Message}");
        }
    }

    // 清空所有存档坐标（重置功能）
    private void ClearAllSaveSlots()
    {
        try
        {
            // 清空内存中的存档数据
            saveSlots.Clear();
            extendedSaveSlots.Clear();

            // 保存空数据到JSON文件
            SavePersistentData();

            Logger?.LogWarning("已清空所有存档坐标！| All save slots cleared!");
            LogInfo("所有传送位置已重置，可以重新保存坐标 | All teleport positions reset, you can save new coordinates");

            // 刷新UI
            uiManager?.RefreshSlotList();
        }
        catch (Exception ex)
        {
            Logger?.LogError($"清空存档坐标时发生错误 | Error clearing save slots: {ex.Message}");
        }
    }

    // 初始化UI管理器
    private void InitializeUIManager()
    {
        try
        {
            // 如果已经有UI管理器，先取消订阅避免重复订阅
            UnsubscribeUIEvents();

            TeleportUIManager.Initialize(Logger);
            uiManager = TeleportUIManager.Instance;

            if (uiManager != null)
            {
                // 订阅UI事件
                uiManager.OnTeleportToSlot += OnUITeleportToSlot;
                uiManager.OnDeleteSlot += OnUIDeleteSlot;
                uiManager.OnOverwriteSlot += OnUIOverwriteSlot;
                uiManager.OnSaveCurrentPosition += OnUISaveCurrentPosition;
                uiManager.OnClearAllSlots += OnUIClearAllSlots;
                uiManager.OnSafeRespawn += OnUISafeRespawn;
                uiManager.OnBenchTeleport += OnUIBenchTeleport;
                uiManager.OnHardcodedTeleport += OnUIHardcodedTeleport;

                Logger?.LogInfo("UI管理器初始化完成");
            }
        }
        catch (Exception ex)
        {
            Logger?.LogError($"初始化UI管理器时发生错误: {ex.Message}");
        }
    }

    // 取消订阅UI事件
    private void UnsubscribeUIEvents()
    {
        try
        {
            if (uiManager != null)
            {
                uiManager.OnTeleportToSlot -= OnUITeleportToSlot;
                uiManager.OnDeleteSlot -= OnUIDeleteSlot;
                uiManager.OnOverwriteSlot -= OnUIOverwriteSlot;
                uiManager.OnSaveCurrentPosition -= OnUISaveCurrentPosition;
                uiManager.OnClearAllSlots -= OnUIClearAllSlots;
                uiManager.OnSafeRespawn -= OnUISafeRespawn;
                uiManager.OnBenchTeleport -= OnUIBenchTeleport;
                uiManager.OnHardcodedTeleport -= OnUIHardcodedTeleport;

                Logger?.LogInfo("UI事件订阅已取消");
            }
        }
        catch (Exception ex)
        {
            Logger?.LogError($"取消UI事件订阅时发生错误: {ex.Message}");
        }
    }

    // 组件销毁时的清理工作
    private void OnDestroy()
    {
        try
        {
            // 取消所有事件订阅
            UnsubscribeUIEvents();

            Logger?.LogInfo("TeleportMod 组件销毁清理完成");
        }
        catch (Exception ex)
        {
            Logger?.LogError($"组件销毁清理时发生错误: {ex.Message}");
        }
    }

    // UI事件处理 - 传送到指定存档
    private void OnUITeleportToSlot(string slotId)
    {
        try
        {
            if (!CanPerformTeleportOperations())
            {
                Logger?.LogWarning("当前状态不允许传送操作");
                return;
            }

            // 先关闭所有界面，避免传送后游戏卡住
            CloseAllMenusBeforeTeleport();

            // 检查是否是传统存档
            if (slotId.StartsWith("traditional_"))
            {
                string numberStr = slotId.Substring("traditional_".Length);
                if (int.TryParse(numberStr, out int slotNumber))
                {
                    LoadFromSlot(slotNumber);
                    LogInfo($"从UI传送到传统存档: 存档槽 {slotNumber}");
                }
            }
            // 检查扩展存档
            else if (extendedSaveSlots.ContainsKey(slotId) && extendedSaveSlots[slotId].hasData)
            {
                var slot = extendedSaveSlots[slotId];
                TeleportToExtendedSlot(slot);
                LogInfo($"从UI传送到存档: {slot.displayName}");
            }
            else
            {
                Logger?.LogWarning($"存档 {slotId} 不存在或无数据");
            }
        }
        catch (Exception ex)
        {
            Logger?.LogError($"UI传送操作失败: {ex.Message}");
        }
    }

    // UI事件处理 - 删除指定存档
    private void OnUIDeleteSlot(string slotId)
    {
        try
        {
            // 检查是否是传统存档
            if (slotId.StartsWith("traditional_"))
            {
                string numberStr = slotId.Substring("traditional_".Length);
                if (int.TryParse(numberStr, out int slotNumber))
                {
                    if (saveSlots.ContainsKey(slotNumber))
                    {
                        saveSlots.Remove(slotNumber);
                        SavePersistentData();
                        uiManager?.RefreshSlotList();
                        LogInfo($"已删除传统存档: 存档槽 {slotNumber}");
                        return;
                    }
                }
            }

            // 检查扩展存档
            if (extendedSaveSlots.ContainsKey(slotId))
            {
                var slotName = extendedSaveSlots[slotId].displayName;
                extendedSaveSlots.Remove(slotId);
                SavePersistentData();
                uiManager?.RefreshSlotList();
                LogInfo($"已删除存档: {slotName} ({slotId})");
            }
            else
            {
                Logger?.LogWarning($"存档 {slotId} 不存在");
            }
        }
        catch (Exception ex)
        {
            Logger?.LogError($"删除存档失败: {ex.Message}");
        }
    }

    // UI事件处理 - 覆盖指定存档
    private void OnUIOverwriteSlot(string slotId)
    {
        try
        {
            if (!CanPerformTeleportOperations())
            {
                Logger?.LogWarning("当前状态不允许覆盖操作");
                return;
            }

            if (HeroController.instance != null && GameManager.instance != null)
            {
                Vector3 currentPosition = HeroController.instance.transform.position;
                string currentScene = GameManager.instance.sceneName;

                // 检查是否是传统存档
                if (slotId.StartsWith("traditional_"))
                {
                    // 覆盖传统存档（使用正确的构造函数设置时间戳）
                    int slotNumber = int.Parse(slotId.Replace("traditional_", ""));
                    var newSlot = new SaveSlot(currentPosition, currentScene);  // 使用构造函数自动设置当前时间

                    saveSlots[slotNumber] = newSlot;
                    SavePersistentData();
                    LogInfo($"已覆盖传统存档槽 {slotNumber}: {currentScene} ({currentPosition.x:F1}, {currentPosition.y:F1})");
                }
                else if (extendedSaveSlots.ContainsKey(slotId))
                {
                    // 覆盖扩展存档，生成新的场景+时间戳名称，但保持原序号，不浪费计数器
                    var oldSlot = extendedSaveSlots[slotId];
                    var newSlot = new ExtendedSaveSlot(currentPosition, currentScene, assignNewSerial: false);  // 不分配新序号
                    newSlot.serialNumber = oldSlot.serialNumber;  // 保持原序号，不改变

                    extendedSaveSlots[slotId] = newSlot;
                    SavePersistentData();
                    LogInfo($"已覆盖存档: {newSlot.displayName} ({slotId}), 保持序号: {newSlot.serialNumber}, 计数器未增加");
                }
                else
                {
                    Logger?.LogWarning($"存档 {slotId} 不存在，无法覆盖");
                    return;
                }

                // 刷新UI显示
                uiManager?.RefreshSlotList();
            }
            else
            {
                Logger?.LogWarning("无法获取当前位置信息");
            }
        }
        catch (Exception ex)
        {
            Logger?.LogError($"覆盖存档失败: {ex.Message}");
        }
    }

    // UI事件处理 - 保存当前位置
    private void OnUISaveCurrentPosition()
    {
        try
        {
            if (!CanPerformTeleportOperations())
            {
                Logger?.LogWarning("当前状态不允许保存操作");
                return;
            }

            if (HeroController.instance != null && GameManager.instance != null)
            {
                Vector3 currentPosition = HeroController.instance.transform.position;
                string currentScene = GameManager.instance.sceneName;

                // 生成唯一ID
                string slotId = Guid.NewGuid().ToString();

                // 创建扩展存档
                var newSlot = new ExtendedSaveSlot(currentPosition, currentScene);
                extendedSaveSlots[slotId] = newSlot;

                SavePersistentData();
                PlaySaveSound();
                uiManager?.RefreshSlotList();

                LogInfo($"UI保存新存档: {newSlot.displayName} 在 {currentScene} ({currentPosition.x:F1}, {currentPosition.y:F1})");
            }
        }
        catch (Exception ex)
        {
            Logger?.LogError($"UI保存操作失败: {ex.Message}");
        }
    }

    // UI事件处理 - 清空所有存档
    private void OnUIClearAllSlots()
    {
        try
        {
            ClearAllSaveSlots();
        }
        catch (Exception ex)
        {
            Logger?.LogError($"UI清空操作失败: {ex.Message}");
        }
    }

    // UI事件处理 - 安全重生
    private void OnUISafeRespawn()
    {
        try
        {
            if (!CanPerformTeleportOperations())
            {
                Logger?.LogWarning("当前状态不允许安全重生操作");
                return;
            }

            // 先关闭所有界面，确保安全传送
            CloseAllMenusBeforeTeleport();

            RespawnToSafeEntryPoint();
            LogInfo("从UI执行安全重生");
        }
        catch (Exception ex)
        {
            Logger?.LogError($"UI安全重生操作失败: {ex.Message}");
        }
    }

    // UI事件处理 - 传送到椅子
    private void OnUIBenchTeleport()
    {
        try
        {
            if (!CanPerformTeleportOperations())
            {
                Logger?.LogWarning("当前状态不允许椅子传送操作");
                return;
            }

            // 先关闭所有界面，确保安全传送
            CloseAllMenusBeforeTeleport();

            TeleportToBench();
            LogInfo("从UI执行椅子传送");
        }
        catch (Exception ex)
        {
            Logger?.LogError($"UI椅子传送操作失败: {ex.Message}");
        }
    }

    // UI事件处理 - 预设坐标传送
    private void OnUIHardcodedTeleport()
    {
        try
        {
            if (!CanPerformTeleportOperations())
            {
                Logger?.LogWarning("当前状态不允许预设传送操作");
                return;
            }

            // 先关闭所有界面，确保安全传送
            CloseAllMenusBeforeTeleport();

            TeleportToHardcodedPosition();
            LogInfo("从UI执行预设坐标传送");
        }
        catch (Exception ex)
        {
            Logger?.LogError($"UI预设传送操作失败: {ex.Message}");
        }
    }

    // 统一的菜单关闭方法 - 确保传送前所有界面都被关闭
    private void CloseAllMenusBeforeTeleport()
    {
        try
        {
            // 关闭自定义UI界面
            uiManager?.SetUIVisible(false);

            // 检查并关闭ESC菜单（防止手柄呼出ESC菜单导致的冲突）
            var uiManagerInstance = UIManager.instance;
            if (uiManagerInstance != null && uiManagerInstance.uiState.Equals(UIState.PAUSED))
            {
                uiManagerInstance.SetState(UIState.PLAYING);
                LogInfo("检测到ESC菜单打开，已强制关闭以避免传送冲突");
            }

            LogInfo("已关闭所有界面，准备安全传送");
        }
        catch (Exception menuEx)
        {
            Logger?.LogError($"关闭界面时发生错误: {menuEx.Message}");
        }
    }

    // 传送到扩展存档位置
    private void TeleportToExtendedSlot(ExtendedSaveSlot slot)
    {
        try
        {
            string currentScene = GameManager.instance?.sceneName ?? "";
            Vector3 targetPosition = slot.position;
            string targetScene = slot.scene;

            LogInfo($"准备传送到扩展存档: {slot.displayName} -> {targetPosition} 在场景: {targetScene}");

            // 检查是否需要切换场景
            if (!string.IsNullOrEmpty(targetScene) && currentScene != targetScene)
            {
                LogInfo($"需要切换场景: {currentScene} -> {targetScene}");
                StartCoroutine(TeleportWithSceneChange(targetScene, targetPosition));
            }
            else
            {
                // 在同一场景，直接传送
                LogInfo("在当前场景传送");
                PerformTeleport(targetPosition);
            }
        }
        catch (Exception ex)
        {
            Logger?.LogError($"传送到扩展存档时发生错误: {ex.Message}");
        }
    }

    // 获取所有存档数据（供UI使用）
    public static Dictionary<string, ExtendedSaveSlot> GetAllSaveSlots()
    {
        var allSlots = new Dictionary<string, ExtendedSaveSlot>();

        try
        {
            // 添加传统存档（转换为扩展格式）
            foreach (var kvp in saveSlots)
            {
                if (kvp.Value.hasData)
                {
                    string traditionalId = $"traditional_{kvp.Key}";
                    var extendedSlot = new ExtendedSaveSlot(kvp.Value);
                    // 传统存档保持原有格式，优化英文翻译
                    extendedSlot.displayName = $"快捷存档 {kvp.Key} | Hotkey {kvp.Key}";
                    allSlots[traditionalId] = extendedSlot;
                }
            }

            // 添加扩展存档
            foreach (var kvp in extendedSaveSlots)
            {
                if (kvp.Value.hasData)
                {
                    allSlots[kvp.Key] = kvp.Value;
                }
            }
        }
        catch (Exception ex)
        {
            Logger?.LogError($"获取所有存档数据时发生错误: {ex.Message}");
        }

        return allSlots;
    }

    // 处理紧急重启输入（独立方法，优先级最高）
    private void HandleEmergencyRestartInput()
    {
        try
        {
            // 紧急返回主菜单：Ctrl+F9（固定按键）
            if (Input.GetKey(KeyCode.LeftControl) && Input.GetKeyDown(KeyCode.F9))
            {
                EmergencyReturnToMainMenu();
                return;
            }
        }
        catch (Exception ex)
        {
            Logger?.LogError($"处理紧急重启输入时发生错误 | Error handling emergency restart input: {ex.Message}");
        }
    }

    // 紧急返回主菜单功能
    private void EmergencyReturnToMainMenu()
    {
        try
        {
            Logger?.LogWarning("=== 紧急返回主菜单 | EMERGENCY RETURN TO MAIN MENU ===");
            Logger?.LogWarning("正在强制返回主菜单，不保存当前进度！| Force returning to main menu without saving current progress!");

            // 检查GameManager是否可用
            if (GameManager.instance == null)
            {
                Logger?.LogError("GameManager实例未找到，无法返回主菜单 | GameManager instance not found, cannot return to main menu");
                return;
            }

            // 使用GameManager的不保存返回主菜单方法
            GameManager.instance.ReturnToMainMenuNoSave();
            Logger?.LogInfo("已触发返回主菜单 | Return to main menu triggered");
        }
        catch (Exception ex)
        {
            Logger?.LogError($"紧急返回主菜单时发生错误 | Error during emergency return to main menu: {ex.Message}");
        }
    }

    // 直接传送到椅子功能
    private void TeleportToBench()
    {
        try
        {
            if (HeroController.instance == null || GameManager.instance == null)
            {
                Logger?.LogWarning("HeroController 或 GameManager 未找到，无法传送到椅子");
                return;
            }

            LogInfo("=== 传送到椅子 | TELEPORT TO BENCH ===");

            // 获取椅子位置信息
            var benchInfo = GetBenchPositionAndScene();
            if (benchInfo.position == Vector3.zero || string.IsNullOrEmpty(benchInfo.scene))
            {
                Logger?.LogWarning("未找到有效的椅子位置或场景信息 | No valid bench position or scene found");
                return;
            }

            // 为椅子位置添加Y轴偏移，防止卡地里
            Vector3 safeBenchPosition = ApplyBenchSafeOffset(benchInfo.position);

            string currentScene = GameManager.instance.sceneName;
            LogInfo($"准备传送到椅子: {safeBenchPosition} 在场景: {benchInfo.scene}");

            // 检查是否需要切换场景
            if (!string.IsNullOrEmpty(benchInfo.scene) && currentScene != benchInfo.scene)
            {
                LogInfo($"需要切换场景传送到椅子: {currentScene} -> {benchInfo.scene}");
                StartCoroutine(TeleportWithSceneChange(benchInfo.scene, safeBenchPosition));
            }
            else
            {
                // 在同一场景，直接传送
                LogInfo("在当前场景传送到椅子");
                PerformTeleport(safeBenchPosition);
            }
        }
        catch (Exception ex)
        {
            Logger?.LogError($"传送到椅子时发生错误 | Error during teleport to bench: {ex.Message}");
        }
    }


    // 将字符串转换为KeyCode
    private KeyCode ParseKeyCode(string keyString)
    {
        try
        {
            return (KeyCode)System.Enum.Parse(typeof(KeyCode), keyString, true);
        }
        catch
        {
            Logger?.LogWarning($"无法解析按键设置: {keyString}，使用默认值 | Cannot parse key setting: {keyString}, using default");
            return KeyCode.None;
        }
    }

    // 检查修饰键是否被按下
    private bool IsModifierKeyPressed(string modifierKeyString)
    {
        KeyCode keyCode = ParseKeyCode(modifierKeyString);
        if (keyCode == KeyCode.None) return false;

        return Input.GetKey(keyCode);
    }

    // 获取配置的存档槽按键
    private KeyCode GetSlotKey(int slotNumber)
    {
        string keyString = slotNumber switch
        {
            1 => slot1Key?.Value ?? "Alpha1",
            2 => slot2Key?.Value ?? "Alpha2",
            3 => slot3Key?.Value ?? "Alpha3",
            4 => slot4Key?.Value ?? "Alpha4",
            5 => slot5Key?.Value ?? "Alpha5",
            _ => "None"
        };
        return ParseKeyCode(keyString);
    }


    // Alt+6 功能：重新进入当前场景的安全入口点
    private void RespawnToSafeEntryPoint()
    {
        try
        {
            if (HeroController.instance == null || GameManager.instance == null)
            {
                Logger?.LogWarning("HeroController 或 GameManager 未找到，无法执行安全重生");
                return;
            }

            string currentScene = GameManager.instance.sceneName;

            // 检查是否是新场景，如果是则重置索引到0
            if (lastUsedScene != currentScene)
            {
                currentEntryPointIndex = 0;
                lastUsedScene = currentScene;
                LogInfo($"检测到新场景，重置入口点索引: {currentScene}");
            }
            else
            {
                LogInfo($"同一场景，继续轮换: {currentScene}，当前索引: {currentEntryPointIndex}");
            }

            LogInfo($"正在重新进入当前场景的安全入口点: {currentScene}");

            // 获取当前场景的下一个安全入口点（轮换）
            string? safeEntryPoint = GetNextSafeEntryPointForCurrentScene();
            if (string.IsNullOrEmpty(safeEntryPoint))
            {
                Logger?.LogWarning("未找到当前场景的安全入口点，使用椅子位置");
                var benchInfo = GetBenchPositionAndScene();
                if (benchInfo.position != Vector3.zero && !string.IsNullOrEmpty(benchInfo.scene))
                {
                    // 为椅子位置添加Y轴偏移，防止卡地里
                    Vector3 safeBenchPosition = ApplyBenchSafeOffset(benchInfo.position);

                    if (benchInfo.scene == currentScene)
                    {
                        PerformTeleport(safeBenchPosition);
                    }
                    else
                    {
                        StartCoroutine(TeleportWithSceneChange(benchInfo.scene, safeBenchPosition));
                    }
                }
                return;
            }

            // 使用轮换选择的安全入口点重新进入当前场景
            LogInfo($"使用安全入口点 {currentEntryPointIndex}: {safeEntryPoint}");
            StartCoroutine(TeleportWithSceneChange(currentScene, Vector3.zero, safeEntryPoint));
        }
        catch (Exception ex)
        {
            Logger?.LogError($"执行安全重生时发生错误: {ex.Message}");
        }
    }

    // Alt+- 功能：传送到硬编码的预设坐标
    private void TeleportToHardcodedPosition()
    {
        try
        {
            if (HeroController.instance == null || GameManager.instance == null)
            {
                Logger?.LogWarning("HeroController 或 GameManager 未找到，无法执行硬编码传送");
                return;
            }

            // 硬编码的预设坐标
            Vector3 targetPosition = new Vector3(71.42231f, 9.597684f, 0.004f);
            string targetScene = "Bellway_01";

            LogInfo($"执行硬编码传送到: {targetPosition} 在场景: {targetScene}");

            // 检查是否需要切换场景
            string currentScene = GameManager.instance.sceneName;
            if (!string.IsNullOrEmpty(targetScene) && currentScene != targetScene)
            {
                LogInfo($"需要切换场景进行硬编码传送: {currentScene} -> {targetScene}");
                StartCoroutine(TeleportWithSceneChange(targetScene, targetPosition));
            }
            else
            {
                // 在同一场景，直接传送
                LogInfo("在当前场景执行硬编码传送");
                PerformTeleport(targetPosition);
            }
        }
        catch (Exception ex)
        {
            Logger?.LogError($"执行硬编码传送时发生错误: {ex.Message}");
        }
    }

    // Alt+9 功能：传送到深渊底部的固定椅子坐标
    private void TeleportToAbyssBench()
    {
        try
        {
            if (HeroController.instance == null || GameManager.instance == null)
            {
                Logger?.LogWarning("HeroController 或 GameManager 未找到，无法传送到深渊椅子");
                return;
            }

            Vector3 targetPosition = new Vector3(64.3553f, 72.56767f, 0.004f);
            const string targetScene = "Shadow_02";
            LogInfo($"执行传送到深渊椅子: {targetPosition} 在场景: {targetScene}");

            if (GameManager.instance.sceneName != targetScene)
            {
                StartCoroutine(TeleportWithSceneChange(targetScene, targetPosition));
            }
            else
            {
                PerformTeleport(targetPosition);
            }
        }
        catch (Exception ex)
        {
            Logger?.LogError($"传送到深渊椅子时发生错误: {ex.Message}");
        }
    }

    // 检查并修复当前场景的位置安全性
    private Vector3 CheckAndFixPositionInCurrentScene(Vector3 targetPosition, int slotNumber)
    {
        try
        {
            if (HeroController.instance == null) return targetPosition;

            // 检查位置是否安全
            if (IsPositionSafe(targetPosition))
            {
                LogInfo($"档位 {slotNumber} 位置安全: {targetPosition}");
                return targetPosition;
            }

            // 位置不安全，寻找附近的安全位置
            Logger?.LogWarning($"档位 {slotNumber} 位置不安全，正在查找安全位置: {targetPosition}");
            Vector3 safePosition = FindSafePositionNearby(targetPosition);

            if (safePosition != Vector3.zero)
            {
                // 找到安全位置，更新存档槽（保持原始时间戳）
                string currentScene = GameManager.instance.sceneName;
                DateTime originalTime = saveSlots[slotNumber].saveTime;
                saveSlots[slotNumber] = new SaveSlot(safePosition, currentScene, originalTime);
                LogInfo($"档位 {slotNumber} 已修正为安全位置: {targetPosition} -> {safePosition}");
                return safePosition;
            }
            else
            {
                Logger?.LogWarning($"档位 {slotNumber} 无法找到安全位置，将在传送后尝试修复");
                return targetPosition;
            }
        }
        catch (Exception ex)
        {
            Logger?.LogError($"检查位置安全性时发生错误: {ex.Message}");
            return targetPosition;
        }
    }

    // 检查位置是否安全（不会卡在地形中）
    private bool IsPositionSafe(Vector3 position)
    {
        try
        {
            var heroCollider = HeroController.instance?.GetComponent<Collider2D>();
            if (heroCollider == null) return true; // 如果获取不到碰撞器，假设安全

            var groundLayerMask = LayerMask.GetMask("Terrain");

            // 检查该位置是否与地形重叠
            var overlap = Physics2D.OverlapBox(
                position,
                heroCollider.bounds.size,
                0f,
                groundLayerMask
            );

            bool isSafe = overlap == null;
            LogInfo($"位置安全检查: {position} -> {(isSafe ? "安全" : "不安全")}");
            return isSafe;
        }
        catch (Exception ex)
        {
            Logger?.LogError($"检查位置安全性时发生错误: {ex.Message}");
            return true; // 出错时假设安全
        }
    }

    // 获取当前场景的下一个安全入口点（轮换模式）
    private string? GetNextSafeEntryPointForCurrentScene()
    {
        try
        {
            var allSafeEntryPoints = GetAllSafeEntryPointsForCurrentScene();
            if (allSafeEntryPoints == null || allSafeEntryPoints.Count == 0)
            {
                LogInfo("当前场景没有可用的安全入口点");
                return null;
            }

            // 循环选择入口点
            if (currentEntryPointIndex >= allSafeEntryPoints.Count)
            {
                currentEntryPointIndex = 0; // 回到开头
            }

            string selectedEntryPoint = allSafeEntryPoints[currentEntryPointIndex];
            LogInfo($"选择安全入口点 {currentEntryPointIndex + 1}/{allSafeEntryPoints.Count}: {selectedEntryPoint}");

            // 为下次使用准备索引
            currentEntryPointIndex++;

            return selectedEntryPoint;
        }
        catch (Exception ex)
        {
            Logger?.LogError($"获取下一个安全入口点时发生错误: {ex.Message}");
            return null;
        }
    }

    // 获取当前场景所有可用的安全入口点列表
    private List<string>? GetAllSafeEntryPointsForCurrentScene()
    {
        try
        {
            // 获取当前场景的所有TransitionPoint
            var transitionPoints = TransitionPoint.TransitionPoints;
            if (transitionPoints == null || transitionPoints.Count == 0)
            {
                LogInfo("当前场景没有TransitionPoint");
                return null;
            }

            var safeEntryPoints = new List<string>();

            // 优先添加门类型的入口点
            var doorEntries = transitionPoints.Where(tp => tp != null &&
                tp.name.Contains("door") &&
                !tp.isInactive).ToList();

            foreach (var door in doorEntries)
            {
                safeEntryPoints.Add(door.name);
                LogInfo($"找到门入口点: {door.name}");
            }

            // 然后添加其他方向入口点
            var otherEntries = transitionPoints.Where(tp => tp != null &&
                !tp.isInactive &&
                !tp.name.Contains("door") &&
                (tp.name.Contains("left") || tp.name.Contains("right") ||
                 tp.name.Contains("top") || tp.name.Contains("bot"))).ToList();

            foreach (var entry in otherEntries)
            {
                safeEntryPoints.Add(entry.name);
                LogInfo($"找到其他入口点: {entry.name}");
            }

            // 如果还是没有，添加所有可用的入口点
            if (safeEntryPoints.Count == 0)
            {
                var allAvailable = transitionPoints.Where(tp => tp != null && !tp.isInactive).ToList();
                foreach (var tp in allAvailable)
                {
                    safeEntryPoints.Add(tp.name);
                    LogInfo($"找到可用入口点: {tp.name}");
                }
            }

            LogInfo($"总共找到 {safeEntryPoints.Count} 个安全入口点");
            return safeEntryPoints.Count > 0 ? safeEntryPoints : null;
        }
        catch (Exception ex)
        {
            Logger?.LogError($"获取所有安全入口点时发生错误: {ex.Message}");
            return null;
        }
    }

    // 获取当前场景的安全入口点（保持原有方法用于其他地方）
    private string? GetSafeEntryPointForCurrentScene()
    {
        try
        {
            // 获取当前场景的所有TransitionPoint
            var transitionPoints = TransitionPoint.TransitionPoints;
            if (transitionPoints == null || transitionPoints.Count == 0)
            {
                LogInfo("当前场景没有TransitionPoint");
                return null;
            }

            // 优先查找门类型的入口点
            var doorEntries = transitionPoints.Where(tp => tp != null &&
                tp.name.Contains("door") &&
                !tp.isInactive).ToList();

            if (doorEntries.Count > 0)
            {
                LogInfo($"找到门入口点: {doorEntries[0].name}");
                return doorEntries[0].name;
            }

            // 如果没有门，查找其他入口点（left, right, top, bottom）
            var otherEntries = transitionPoints.Where(tp => tp != null &&
                !tp.isInactive &&
                (tp.name.Contains("left") || tp.name.Contains("right") ||
                 tp.name.Contains("top") || tp.name.Contains("bot"))).ToList();

            if (otherEntries.Count > 0)
            {
                LogInfo($"找到其他入口点: {otherEntries[0].name}");
                return otherEntries[0].name;
            }

            // 最后fallback到第一个可用的入口点
            var firstAvailable = transitionPoints.FirstOrDefault(tp => tp != null && !tp.isInactive);
            if (firstAvailable != null)
            {
                LogInfo($"使用第一个可用入口点: {firstAvailable.name}");
                return firstAvailable.name;
            }

            return null;
        }
        catch (Exception ex)
        {
            Logger?.LogError($"获取安全入口点时发生错误: {ex.Message}");
            return null;
        }
    }

    private void SaveToSlot(int slotNumber)
    {
        try
        {
            if (HeroController.instance != null && GameManager.instance != null)
            {
                Vector3 currentPosition = HeroController.instance.transform.position;
                string currentScene = GameManager.instance.sceneName;

                saveSlots[slotNumber] = new SaveSlot(currentPosition, currentScene);
                LogInfo($"档位 {slotNumber} 已保存: {currentPosition} 在场景: {currentScene}");

                // 播放存档音效提示
                PlaySaveSound();

                // 保存持久化数据
                SavePersistentData();

                // 如果UI界面可见，刷新界面显示
                if (uiManager != null && uiManager.IsUIVisible)
                {
                    uiManager.RefreshSlotList();
                    LogInfo($"UI界面已刷新，显示新保存的档位 {slotNumber}");
                }
            }
            else
            {
                Logger?.LogWarning("HeroController 或 GameManager 未找到，无法保存位置");
            }
        }
        catch (Exception ex)
        {
            Logger?.LogError($"保存档位 {slotNumber} 时发生错误: {ex.Message}");
        }
    }

    // 优化后的音频播放 - 使用缓存避免重复加载
    private void PlaySaveSound()
    {
        try
        {
            // 检查音量设置，如果为0则不播放音效
            if (audioVolume?.Value <= 0f)
            {
                LogInfo("音效音量设置为0，跳过音效播放");
                return;
            }

            // 检查音频播放冷却时间，防止快速连续播放
            float currentTime = Time.time;
            if (currentTime - lastSaveAudioTime < AUDIO_COOLDOWN)
            {
                LogInfo("音频播放在冷却中，跳过此次播放");
                return;
            }
            lastSaveAudioTime = currentTime;

            // 确保音频播放器存在
            EnsureAudioPlayer();

            // 使用缓存的AudioClip
            if (cachedSaveAudioClip != null && audioPlayerSource != null)
            {
                // 使用PlayOneShot避免中断当前播放，使用配置的音量
                audioPlayerSource.PlayOneShot(cachedSaveAudioClip, audioVolume?.Value ?? 0.5f);
                LogInfo($"使用缓存音频播放存档音效，音量: {audioVolume?.Value ?? 0.5f}");
            }
            else
            {
                LogInfo("音频未预加载完成，跳过播放");
            }
        }
        catch (Exception ex)
        {
            Logger?.LogError($"播放存档音效时发生错误: {ex.Message}");
        }
    }

    // 确保音频播放器存在（复用机制）
    private void EnsureAudioPlayer()
    {
        if (audioPlayerObject == null || audioPlayerSource == null)
        {
            // 创建新的音频播放器（只创建一次）
            audioPlayerObject = new GameObject("TeleportAudioPlayer");
            audioPlayerSource = audioPlayerObject.AddComponent<AudioSource>();

            // 配置为2D音频
            audioPlayerSource.volume = audioVolume?.Value ?? 0.5f;
            audioPlayerSource.spatialBlend = 0f; // 2D音频
            audioPlayerSource.playOnAwake = false;
            audioPlayerSource.loop = false;

            // 设置为不销毁，可以复用
            UnityEngine.Object.DontDestroyOnLoad(audioPlayerObject);
            LogInfo("创建并复用音频播放器对象");
        }
    }

    // 预加载音频文件协程 - 启动时一次性加载
    private IEnumerator PreloadAudioClip()
    {
        LogInfo("开始预加载音频文件...");

        // 根据配置选择音频文件名（去掉Teleport.前缀）
        var fileName = (enableEasterEggAudio?.Value == true) ? "manbo.wav" : "Gamesave.wav";

        // 获取当前DLL所在的目录（BepInEx\plugins或其子目录）
        var assembly = Assembly.GetExecutingAssembly();
        string dllDirectory = Path.GetDirectoryName(assembly.Location);
        string audioFilePath = Path.Combine(dllDirectory, fileName);
        LogInfo($"选择音频文件: {fileName} (彩蛋音效: {enableEasterEggAudio?.Value})");
        LogInfo($"音频文件路径: {audioFilePath}");

        string tempPath = "";

        try
        {
            // 检查音频文件是否存在
            if (!File.Exists(audioFilePath))
            {
                Logger?.LogWarning($"未找到音频文件: {audioFilePath}");
                yield break;
            }

            // 直接读取音频文件
            byte[] audioData = File.ReadAllBytes(audioFilePath);
            LogInfo($"成功读取音频文件，大小: {audioData.Length} 字节");

            // 创建临时文件用于加载（只在预加载时创建一次）
            tempPath = Path.GetTempFileName() + ".wav";
            File.WriteAllBytes(tempPath, audioData);
        }
        catch (Exception ex)
        {
            Logger?.LogError($"读取音频文件时发生错误: {ex.Message}");
            yield break;
        }

        // 使用UnityWebRequest加载音频（在try-catch外部）
        using (UnityWebRequest request = UnityWebRequestMultimedia.GetAudioClip("file://" + tempPath, AudioType.WAV))
        {
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                cachedSaveAudioClip = DownloadHandlerAudioClip.GetContent(request);
                if (cachedSaveAudioClip != null)
                {
                    // 设置音频不销毁，保持缓存
                    UnityEngine.Object.DontDestroyOnLoad(cachedSaveAudioClip);
                    LogInfo($"音频预加载成功 - 长度: {cachedSaveAudioClip.length}秒, 频率: {cachedSaveAudioClip.frequency}, 声道: {cachedSaveAudioClip.channels}");
                }
                else
                {
                    Logger?.LogWarning("无法获取AudioClip");
                }
            }
            else
            {
                Logger?.LogError($"预加载音频失败: {request.error}");
            }
        }

        // 清理临时文件
        try
        {
            if (!string.IsNullOrEmpty(tempPath) && File.Exists(tempPath))
            {
                File.Delete(tempPath);
                LogInfo("已清理预加载临时文件");
            }
        }
        catch (Exception ex)
        {
            Logger?.LogError($"删除预加载临时文件失败: {ex.Message}");
        }
    }

    private void LoadFromSlot(int slotNumber)
    {
        try
        {
            if (HeroController.instance == null || GameManager.instance == null)
            {
                Logger?.LogWarning("HeroController 或 GameManager 未找到，无法传送");
                return;
            }

            Vector3 targetPosition;
            string targetScene;

            // 检查指定档位是否有存档数据
            if (saveSlots.ContainsKey(slotNumber) && saveSlots[slotNumber].hasData)
            {
                // 有存档数据，传送到存档位置
                var slot = saveSlots[slotNumber];
                targetPosition = slot.position;
                targetScene = slot.scene;
                LogInfo($"准备传送到档位 {slotNumber}: {targetPosition} 在场景: {targetScene}");
            }
            else
            {
                // 没有存档数据，回退到椅子传送逻辑
                LogInfo($"档位 {slotNumber} 没有存档数据，传送到椅子位置");
                var benchInfo = GetBenchPositionAndScene();
                targetPosition = benchInfo.position;
                targetScene = benchInfo.scene;

                if (targetPosition == Vector3.zero || string.IsNullOrEmpty(targetScene))
                {
                    Logger?.LogWarning("未找到有效的椅子位置或场景信息");
                    return;
                }

                // 为椅子位置添加Y轴偏移，防止卡地里
                targetPosition = ApplyBenchSafeOffset(targetPosition);
                LogInfo($"椅子位置已添加安全偏移，准备传送到椅子位置: {targetPosition} 在场景: {targetScene}");
            }

            // 检查是否需要切换场景
            string currentScene = GameManager.instance.sceneName;
            if (!string.IsNullOrEmpty(targetScene) && currentScene != targetScene)
            {
                LogInfo($"需要切换场景: {currentScene} -> {targetScene}");
                StartCoroutine(TeleportWithSceneChange(targetScene, targetPosition));
            }
            else
            {
                // 在同一场景，先检查位置安全性
                Vector3 safePosition = CheckAndFixPositionInCurrentScene(targetPosition, slotNumber);
                // 已经预先检查过安全性，直接传送，无需重复检查
                PerformTeleport(safePosition);
            }

        }
        catch (Exception ex)
        {
            Logger?.LogError($"从档位 {slotNumber} 传送时发生错误: {ex.Message}");
        }
    }

    private (Vector3 position, string scene) GetBenchPositionAndScene()
    {
        try
        {
            if (PlayerData.instance == null)
            {
                Logger?.LogWarning("PlayerData 未找到");
                return (Vector3.zero, "");
            }

            string respawnMarkerName = PlayerData.instance.respawnMarkerName;
            string respawnScene = PlayerData.instance.respawnScene;

            if (string.IsNullOrEmpty(respawnMarkerName) || string.IsNullOrEmpty(respawnScene))
            {
                Logger?.LogWarning("未找到椅子标记名称或场景信息");
                return (Vector3.zero, "");
            }

            LogInfo($"查找椅子: {respawnMarkerName} 在场景: {respawnScene}");

            // 检查椅子是否在当前场景
            string currentScene = GameManager.instance?.sceneName ?? "";
            if (currentScene == respawnScene)
            {
                // 椅子在当前场景，直接查找位置
                if (RespawnMarker.Markers != null)
                {
                    var targetMarker = RespawnMarker.Markers
                        .FirstOrDefault(marker => marker != null && marker.gameObject.name == respawnMarkerName);

                    if (targetMarker != null)
                    {
                        LogInfo($"在当前场景找到椅子: {targetMarker.gameObject.name} 位置: {targetMarker.transform.position}");
                        return (targetMarker.transform.position, respawnScene);
                    }
                }
                Logger?.LogWarning($"在当前场景中未找到椅子标记: {respawnMarkerName}");
                return (Vector3.zero, "");
            }
            else
            {
                // 椅子在其他场景，返回场景信息，坐标将在场景切换后获取
                LogInfo($"椅子在其他场景: {respawnScene}，需要切换场景后获取坐标");
                return (Vector3.one, respawnScene); // 使用 Vector3.one 作为占位符，表示需要场景切换后获取真实坐标
            }
        }
        catch (Exception ex)
        {
            Logger?.LogError($"获取椅子位置时发生错误: {ex.Message}");
            return (Vector3.zero, "");
        }
    }

    // 场景切换传送的重载方法
    private IEnumerator TeleportWithSceneChange(string targetScene, Vector3 targetPosition)
    {
        yield return StartCoroutine(TeleportWithSceneChange(targetScene, targetPosition, null));
    }

    // 改进的场景切换传送方法，支持指定入口点
    private IEnumerator TeleportWithSceneChange(string targetScene, Vector3 targetPosition, string? entryPointName)
    {
        LogInfo($"开始场景切换到: {targetScene}");

        // 确定使用的入口点
        string? useEntryPoint = entryPointName;
        if (string.IsNullOrEmpty(useEntryPoint))
        {
            // 如果没有指定入口点，使用通用的门入口
            useEntryPoint = "left1";
        }

        LogInfo($"使用入口点: {useEntryPoint}");

        // 使用 GameManager 的场景切换功能
        try
        {
            GameManager.instance.BeginSceneTransition(new GameManager.SceneLoadInfo
            {
                SceneName = targetScene,
                EntryGateName = useEntryPoint,
                HeroLeaveDirection = GlobalEnums.GatePosition.unknown,
                EntryDelay = 0f,
                Visualization = GameManager.SceneLoadVisualizations.Default,
                AlwaysUnloadUnusedAssets = true
            });
        }
        catch (Exception ex)
        {
            Logger?.LogError($"开始场景切换时发生错误: {ex.Message}");
            yield break;
        }

        // 等待场景加载完成 - 使用游戏内部精确的等待条件
        yield return new WaitWhile(() =>
        {
            var gm = GameManager.instance;
            var hc = HeroController.instance;

            if (gm == null || hc == null) return true;

            // 使用游戏内部标准的等待条件（参考ProjectBenchmark.cs）
            return gm.IsInSceneTransition || !hc.isHeroInPosition || hc.cState.transitioning;
        });

        // 额外等待角色完全可以接受输入
        yield return new WaitUntil(() =>
        {
            var hc = HeroController.instance;
            return hc != null && hc.CanInput();
        });

        // 场景切换完成后，处理目标位置
        try
        {
            Vector3 finalPosition = targetPosition;

            // 如果是椅子传送且使用了占位符坐标，需要重新获取真实坐标
            if (targetPosition == Vector3.one)
            {
                LogInfo("获取椅子在新场景中的真实坐标");
                var benchInfo = GetBenchPositionAndScene();
                if (benchInfo.position != Vector3.zero && benchInfo.position != Vector3.one)
                {
                    // 为椅子位置添加Y轴偏移，防止卡地里
                    finalPosition = ApplyBenchSafeOffset(benchInfo.position);
                    LogInfo($"找到椅子坐标: {benchInfo.position}");
                }
                else
                {
                    Logger?.LogError("场景切换后仍无法找到椅子坐标，使用入口点位置");
                    // 如果找不到椅子坐标，直接使用场景入口点，不再进行额外传送
                    yield break;
                }
            }

            // 如果目标位置是Vector3.zero，说明是Alt+6功能，使用入口点位置，无需额外传送
            if (targetPosition != Vector3.zero)
            {
                LogInfo($"场景切换完成，传送到位置: {finalPosition}");
                PerformSafeTeleport(finalPosition);
            }
            else
            {
                LogInfo("场景切换完成，已在安全入口点位置");
            }
        }
        catch (Exception ex)
        {
            Logger?.LogError($"传送到目标位置时发生错误: {ex.Message}");
        }
    }


    // 安全传送方法，包含位置验证和错误恢复
    private void PerformSafeTeleport(Vector3 targetPosition)
    {
        try
        {
            if (HeroController.instance == null)
            {
                Logger?.LogWarning("HeroController 未找到，无法执行传送");
                return;
            }

            // 执行传送
            PerformTeleport(targetPosition);

            // 等待一帧后检查是否卡在地里
            StartCoroutine(CheckTeleportSafety(targetPosition));
        }
        catch (Exception ex)
        {
            Logger?.LogError($"执行安全传送时发生错误: {ex.Message}");
        }
    }

    // 检查传送后的安全性
    private IEnumerator CheckTeleportSafety(Vector3 originalPosition)
    {
        yield return new WaitForSeconds(0.1f); // 等待物理系统稳定

        try
        {
            if (HeroController.instance == null) yield break;

            // 检查角色是否卡在固体碰撞器中
            var heroCollider = HeroController.instance.GetComponent<Collider2D>();
            if (heroCollider == null) yield break;

            // 检查是否与地形碰撞
            var groundLayerMask = LayerMask.GetMask("Terrain");
            var overlapping = Physics2D.OverlapBox(
                heroCollider.bounds.center,
                heroCollider.bounds.size,
                0f,
                groundLayerMask
            );

            if (overlapping != null)
            {
                Logger?.LogWarning("检测到传送后卡在地形中，尝试修复位置");

                // 尝试向上移动角色到安全位置
                Vector3 safePosition = FindSafePositionNearby(originalPosition);
                if (safePosition != Vector3.zero)
                {
                    PerformTeleport(safePosition);
                    LogInfo($"已修复到安全位置: {safePosition}");
                }
                else
                {
                    Logger?.LogWarning("无法找到安全位置，建议使用Alt+6重新进入场景");
                }
            }
        }
        catch (Exception ex)
        {
            Logger?.LogError($"检查传送安全性时发生错误: {ex.Message}");
        }
    }

    // 在附近查找安全位置
    private Vector3 FindSafePositionNearby(Vector3 originalPosition)
    {
        try
        {
            var heroCollider = HeroController.instance?.GetComponent<Collider2D>();
            if (heroCollider == null) return Vector3.zero;

            var groundLayerMask = LayerMask.GetMask("Terrain");

            // 尝试向上、左、右偏移查找安全位置
            Vector3[] offsets = {
                new Vector3(0, 2f, 0),   // 向上
                new Vector3(0, 4f, 0),   // 向上更远
                new Vector3(-1f, 2f, 0), // 左上
                new Vector3(1f, 2f, 0),  // 右上
                new Vector3(-2f, 0, 0),  // 左侧
                new Vector3(2f, 0, 0),   // 右侧
            };

            foreach (var offset in offsets)
            {
                Vector3 testPosition = originalPosition + offset;

                var overlap = Physics2D.OverlapBox(
                    testPosition,
                    heroCollider.bounds.size,
                    0f,
                    groundLayerMask
                );

                if (overlap == null)
                {
                    LogInfo($"找到安全位置偏移: {offset}");
                    return testPosition;
                }
            }

            return Vector3.zero;
        }
        catch (Exception ex)
        {
            Logger?.LogError($"查找安全位置时发生错误: {ex.Message}");
            return Vector3.zero;
        }
    }

    // 原始传送方法，供内部使用
    private void PerformTeleport(Vector3 targetPosition)
    {
        try
        {
            if (HeroController.instance == null)
            {
                Logger?.LogWarning("HeroController 未找到，无法执行传送");
                return;
            }

            // 执行传送
            HeroController.instance.transform.position = targetPosition;

            // 重置物理速度，避免传送后继续移动
            var rb2d = HeroController.instance.GetComponent<Rigidbody2D>();
            if (rb2d != null)
            {
                rb2d.linearVelocity = Vector2.zero;
            }

            // 重置一些可能导致问题的状态
            if (HeroController.instance.cState != null)
            {
                HeroController.instance.cState.recoiling = false;
                HeroController.instance.cState.transitioning = false;
            }

            LogInfo($"传送完成: {targetPosition}");
        }
        catch (Exception ex)
        {
            Logger?.LogError($"执行传送时发生错误: {ex.Message}");
        }
    }
}

// Harmony补丁类 - 拦截InputHandler的鼠标控制
[HarmonyPatch(typeof(InputHandler), "SetCursorVisible")]
public class InputHandlerPatch
{
    // 前缀补丁：在原方法执行前检查，如果返回false则阻止原方法执行
    public static bool Prefix(bool value)
    {
        try
        {
            // 如果我们的UI界面打开，并且游戏试图隐藏鼠标，则阻止执行
            var uiManager = TeleportUIManager.Instance;
            if (uiManager != null && uiManager.IsUIVisible && !value)
            {
                // 日志记录被拦截的隐藏鼠标操作
                if (Time.frameCount % 180 == 0) // 每3秒记录一次，避免日志刷屏
                {
                    TeleportMod.LogInfo("UI界面打开中，已阻止游戏隐藏鼠标");
                }

                // 强制保持鼠标可见
                Cursor.visible = true;
                Cursor.lockState = CursorLockMode.None;

                return false; // 阻止原方法执行
            }

            return true; // 允许原方法执行
        }
        catch (Exception ex)
        {
            // 如果补丁出错，允许原方法执行以避免游戏崩溃
            TeleportMod.Logger?.LogError($"InputHandler补丁执行时发生错误: {ex.Message}");
            return true;
        }
    }
}
