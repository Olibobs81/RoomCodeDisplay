using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using BepInEx;
using UnityEngine;
using UnityEngine.XR;
using Photon.Pun;

namespace RoomCodeDisplay;

[BepInPlugin("com.olibobs81.roomcodedisplay", "RoomCodeDisplay", "1.0.0")]
public class Main : BaseUnityPlugin
{
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int AddFontResourceEx(string lpszFilename, uint fl, IntPtr pdv);

    private const uint FR_PRIVATE = 0x10;

    public static Main? Instance;

    private bool isToggled = true;
    private bool wasPressed = false;
    private bool showMenu = false;
    private bool fontsLoaded = false;

    private Rect uiRect = new Rect(20, 20, 1000, 300);
    private bool isDraggingDisplay = false;
    private Vector2 dragOffset;

    private Color textColor = Color.white;
    private float red = 1f, green = 1f, blue = 1f;
    private float fontSize = 44f;

    private Rect menuRect = new Rect(100, 100, 300, 460);
    private List<string> allFontNames = new List<string>();
    private string searchFilter = "";
    private Vector2 fontScrollPos;
    private string currentFontName = "Default";
    private FontStyle currentFontStyle = FontStyle.Normal;
    private Font? customFont;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this.gameObject);
            return;
        }

        Instance = this;
    }

    private void LoadAllSystemAndCustomFonts()
    {
        if (fontsLoaded) return;
        fontsLoaded = true;

        HashSet<string> fontSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string font in Font.GetOSInstalledFontNames())
        {
            fontSet.Add(font);
        }

        string customFolderPath = Path.Combine(Paths.PluginPath, "Fonts");
        if (!Directory.Exists(customFolderPath))
        {
            Directory.CreateDirectory(customFolderPath);
        }

        ScanAndRegisterFontDirectory(customFolderPath, fontSet);

        allFontNames = new List<string>(fontSet);
        allFontNames.Sort();
    }

    private void ScanAndRegisterFontDirectory(string dirPath, HashSet<string> fontSet)
    {
        if (!Directory.Exists(dirPath)) return;

        string[] fontFiles = Directory.GetFiles(dirPath, "*.*", SearchOption.AllDirectories);
        foreach (string file in fontFiles)
        {
            string ext = Path.GetExtension(file).ToLower();
            if (ext == ".ttf" || ext == ".otf")
            {
                try
                {
                    AddFontResourceEx(file, FR_PRIVATE, IntPtr.Zero);

                    string internalName = GetInternalFontName(file);
                    if (!string.IsNullOrEmpty(internalName))
                    {
                        fontSet.Add(internalName);
                    }

                    string fileNameNoExt = Path.GetFileNameWithoutExtension(file);
                    if (!string.IsNullOrEmpty(fileNameNoExt))
                    {
                        fontSet.Add(fileNameNoExt);
                    }
                }
                catch { }
            }
        }
    }

    private string GetInternalFontName(string filePath)
    {
        try
        {
            using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (BinaryReader br = new BinaryReader(fs))
            {
                uint version = ReadUInt32BE(br);
                ushort numTables = ReadUInt16BE(br);
                br.BaseStream.Seek(6, SeekOrigin.Current);

                long nameTableOffset = -1;
                for (int i = 0; i < numTables; i++)
                {
                    uint tag = ReadUInt32BE(br);
                    uint checksum = ReadUInt32BE(br);
                    uint offset = ReadUInt32BE(br);
                    uint length = ReadUInt32BE(br);

                    if (tag == 0x6e616d65)
                    {
                        nameTableOffset = offset;
                        break;
                    }
                }

                if (nameTableOffset != -1)
                {
                    fs.Seek(nameTableOffset, SeekOrigin.Begin);
                    ushort format = ReadUInt16BE(br);
                    ushort count = ReadUInt16BE(br);
                    ushort stringOffset = ReadUInt16BE(br);

                    long recordOffset = fs.Position;
                    string familyName = "";

                    for (int i = 0; i < count; i++)
                    {
                        fs.Seek(recordOffset + (i * 12), SeekOrigin.Begin);
                        ushort platformID = ReadUInt16BE(br);
                        ushort encodingID = ReadUInt16BE(br);
                        ushort languageID = ReadUInt16BE(br);
                        ushort nameID = ReadUInt16BE(br);
                        ushort length = ReadUInt16BE(br);
                        ushort offset = ReadUInt16BE(br);

                        if (nameID == 1 || nameID == 4 || nameID == 16)
                        {
                            fs.Seek(nameTableOffset + stringOffset + offset, SeekOrigin.Begin);
                            byte[] bytes = br.ReadBytes(length);

                            string parsedName = "";
                            if (platformID == 3 || platformID == 0)
                            {
                                parsedName = System.Text.Encoding.BigEndianUnicode.GetString(bytes).Trim('\0');
                            }
                            else
                            {
                                parsedName = System.Text.Encoding.UTF8.GetString(bytes).Trim('\0');
                            }

                            if (!string.IsNullOrEmpty(parsedName))
                            {
                                if (nameID == 1) return parsedName;
                                familyName = parsedName;
                            }
                        }
                    }

                    if (!string.IsNullOrEmpty(familyName)) return familyName;
                }
            }
        }
        catch { }

        return Path.GetFileNameWithoutExtension(filePath);
    }

    private static ushort ReadUInt16BE(BinaryReader br)
    {
        byte[] b = br.ReadBytes(2);
        return (ushort)((b[0] << 8) | b[1]);
    }

    private static uint ReadUInt32BE(BinaryReader br)
    {
        byte[] b = br.ReadBytes(4);
        return (uint)((b[0] << 24) | (b[1] << 16) | (b[2] << 8) | b[3]);
    }

    private void Update()
    {
        InputDevices.GetDeviceAtXRNode(XRNode.LeftHand).TryGetFeatureValue(CommonUsages.primary2DAxisClick, out bool leftStickClick);
        InputDevices.GetDeviceAtXRNode(XRNode.RightHand).TryGetFeatureValue(CommonUsages.primary2DAxisClick, out bool rightStickClick);

        bool isPressed = leftStickClick && rightStickClick;

        if (isPressed && !wasPressed)
        {
            isToggled = !isToggled;
        }
        wasPressed = isPressed;
    }

    private void OnGUI()
    {
        Event e = Event.current;

        if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.M || e.keyCode == KeyCode.F5))
        {
            showMenu = !showMenu;

            if (showMenu)
            {
                LoadAllSystemAndCustomFonts();

                menuRect.x = (Screen.width - menuRect.width) / 2f;
                menuRect.y = (Screen.height - menuRect.height) / 2f;
            }

            e.Use();
        }

        HandleDraggingDisplay();

        GUIStyle mainStyle = new GUIStyle();
        mainStyle.fontSize = (int)fontSize;
        mainStyle.fontStyle = currentFontStyle;
        mainStyle.normal.textColor = textColor;
        mainStyle.wordWrap = false;

        if (customFont != null)
        {
            mainStyle.font = customFont;
        }

        string codeText = "ROOM CODE: HIDDEN";
        if (isToggled)
        {
            if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom != null)
            {
                codeText = $"ROOM CODE: {PhotonNetwork.CurrentRoom.Name}\nPLAYERS: {PhotonNetwork.CurrentRoom.PlayerCount}/{PhotonNetwork.CurrentRoom.MaxPlayers}";
            }
            else
            {
                codeText = "NOT CONNECTED";
            }
        }

        GUI.Label(uiRect, codeText, mainStyle);

        if (showMenu)
        {
            menuRect.x = Mathf.Clamp(menuRect.x, 0, Screen.width - menuRect.width);
            menuRect.y = Mathf.Clamp(menuRect.y, 0, Screen.height - menuRect.height);

            menuRect = GUI.Window(0, menuRect, DrawSettingsMenu, "Room Code Settings (M)");
        }
    }

    private void DrawSettingsMenu(int windowID)
    {
        GUI.DragWindow(new Rect(0, 0, 10000, 20));

        GUILayout.Space(10);

        GUILayout.Label("<b>Text Color (RGB)</b>");

        GUILayout.Label($"Red: {red:F2}");
        red = GUILayout.HorizontalSlider(red, 0f, 1f);

        GUILayout.Label($"Green: {green:F2}");
        green = GUILayout.HorizontalSlider(green, 0f, 1f);

        GUILayout.Label($"Blue: {blue:F2}");
        blue = GUILayout.HorizontalSlider(blue, 0f, 1f);

        textColor = new Color(red, green, blue, 1f);

        GUILayout.Space(15);

        GUILayout.Label($"<b>Font Size:</b> {(int)fontSize}");
        float newSize = GUILayout.HorizontalSlider(fontSize, 16f, 96f);
        if (Mathf.Abs(newSize - fontSize) > 0.1f)
        {
            fontSize = newSize;
            if (customFont != null && currentFontName != "Default")
            {
                ApplyFont(currentFontName);
            }
        }

        GUILayout.Space(15);

        GUILayout.Label($"<b>Font:</b> {currentFontName}");

        GUILayout.BeginHorizontal();
        GUILayout.Label("Search:", GUILayout.Width(50));
        searchFilter = GUILayout.TextField(searchFilter);
        GUILayout.EndHorizontal();

        GUILayout.Space(5);

        fontScrollPos = GUILayout.BeginScrollView(fontScrollPos, GUILayout.Height(140));

        foreach (string fontName in allFontNames)
        {
            if (!string.IsNullOrEmpty(searchFilter) && !fontName.ToLower().Contains(searchFilter.ToLower()))
            {
                continue;
            }

            if (GUILayout.Button(fontName))
            {
                ApplyFont(fontName);
            }
        }

        GUILayout.EndScrollView();
    }

    private void ApplyFont(string fontName)
    {
        if (customFont != null)
        {
            Destroy(customFont);
            customFont = null;
        }

        currentFontName = fontName;
        var (baseFamily, style) = ParseFontInfo(fontName);
        currentFontStyle = style;

        Font newFont = Font.CreateDynamicFontFromOSFont(baseFamily, (int)fontSize);

        if (newFont == null)
        {
            newFont = Font.CreateDynamicFontFromOSFont(fontName, (int)fontSize);
        }

        if (newFont != null)
        {
            customFont = newFont;
        }
    }

    private (string baseFamily, FontStyle style) ParseFontInfo(string fullName)
    {
        string lower = fullName.ToLower();
        bool isBold = lower.Contains("bold") || lower.Contains("heavy") || lower.Contains("black") || lower.Contains("semibold") || lower.Contains("extrabold");
        bool isItalic = lower.Contains("italic") || lower.Contains("oblique");

        FontStyle style = FontStyle.Normal;
        if (isBold && isItalic) style = FontStyle.BoldAndItalic;
        else if (isBold) style = FontStyle.Bold;
        else if (isItalic) style = FontStyle.Italic;

        string[] suffixes = new string[]
        {
            " bold italic", " bold", " italic", " oblique", " heavy",
            " black", " semibold", " extrabold", " light", " medium",
            " thin", " regular"
        };

        string baseFamily = fullName;
        foreach (string suffix in suffixes)
        {
            if (baseFamily.ToLower().EndsWith(suffix))
            {
                baseFamily = baseFamily.Substring(0, baseFamily.Length - suffix.Length);
                break;
            }
        }

        return (baseFamily.Trim(), style);
    }

    private void HandleDraggingDisplay()
    {
        Event e = Event.current;

        if (showMenu && menuRect.Contains(e.mousePosition))
            return;

        if (e.type == EventType.MouseDown && e.button == 0 && uiRect.Contains(e.mousePosition))
        {
            isDraggingDisplay = true;
            dragOffset = e.mousePosition - new Vector2(uiRect.x, uiRect.y);
        }
        else if (e.type == EventType.MouseUp && e.button == 0)
        {
            isDraggingDisplay = false;
        }

        if (isDraggingDisplay)
        {
            uiRect.x = e.mousePosition.x - dragOffset.x;
            uiRect.y = e.mousePosition.y - dragOffset.y;
        }
    }
}