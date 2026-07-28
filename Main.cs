using RoomCodeDisplay.Classes;
using RoomCodeDisplay.Patches;
using RoomCodeDisplay.Utilities;
using UnityEngine;
using UnityEngine.XR;
using Photon.Pun;

namespace RoomCodeDisplay;

public class Main : MonoBehaviour
{
    public static Main? Instance;
    public GorillaLog Log = new();

    private bool isToggled = true;
    private bool wasPressed = false;

    // Track the position and size of our text box
    private Rect uiRect = new Rect(20, 20, 400, 100);
    private bool isDragging = false;
    private Vector2 dragOffset;

    private void Start()
    {
        Instance = this;

        HarmonyPatches.Patch();
        Config.Load();
        Application.quitting += Config.Save;

        GorillaTagger.OnPlayerSpawned(() => MethodUtilities.Attempt(OnPlayerSpawned));
    }

    private void OnPlayerSpawned()
    {
        Log.WriteLine($"Server Code display ready!");
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
        // Process mouse dragging events
        HandleDragging();

        GUIStyle style = new GUIStyle();
        style.fontSize = 48;
        style.normal.textColor = Color.white;
        style.fontStyle = FontStyle.Bold;

        GUIStyle shadowStyle = new GUIStyle(style);
        shadowStyle.normal.textColor = Color.black;

        // Determine what text to display based on toggle and server connection
        string codeText = "Current Room: HIDDEN";

        if (isToggled)
        {
            codeText = PhotonNetwork.InRoom ? "Current Room: " + PhotonNetwork.CurrentRoom.Name : "NOT CONNECTED";
        }

        // Draw shadow offset by 2 pixels based on uiRect's dynamic position
        GUI.Label(new Rect(uiRect.x + 2, uiRect.y + 2, uiRect.width, uiRect.height), codeText, shadowStyle);

        // Draw main text
        GUI.Label(uiRect, codeText, style);
    }

    // Handles clicking and dragging the UI on the desktop mirror
    private void HandleDragging()
    {
        Event e = Event.current;

        // Check if the user left-clicks inside our text area
        if (e.type == EventType.MouseDown && e.button == 0 && uiRect.Contains(e.mousePosition))
        {
            isDragging = true;
            // Calculate where inside the box the user clicked so it doesn't snap abruptly
            dragOffset = e.mousePosition - new Vector2(uiRect.x, uiRect.y);
        }
        // Stop dragging when they let go of the left mouse button
        else if (e.type == EventType.MouseUp && e.button == 0)
        {
            isDragging = false;
        }

        // Update the position of the box if they are actively dragging it
        if (isDragging)
        {
            uiRect.x = e.mousePosition.x - dragOffset.x;
            uiRect.y = e.mousePosition.y - dragOffset.y;
        }
    }
}