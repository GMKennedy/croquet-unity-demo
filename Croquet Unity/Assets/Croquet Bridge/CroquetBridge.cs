using System.Collections.Generic;
using System.Collections.Concurrent;
using UnityEngine;

using System;
using System.Text.RegularExpressions;
using Unity.WebRTC;

public class CroquetBridge : MonoBehaviour
{
    public static CroquetBridge theBridge;

    private UniWebView webView;
    public bool croquetReady;
    private string localViewId;
    private CroquetDataChannel dataChannel;

    private Dictionary<int, CroquetObject> croquetObjects = new Dictionary<int, CroquetObject>();

    private TextMesh processOutput;
    private static string debugText = "";
    private string lastDebugText = "dummy";

    //private bool synced = false;

    public CroquetObject avatarObject;
    private string upOrDown;
    private string newUpOrDown;
    private string leftOrRight;
    private string newLeftOrRight;

    public delegate void CustomCreateObjectDelegate(JSONObject data, out GameObject go, out Type coType);
    public static CustomCreateObjectDelegate customCreate = null;

    public delegate bool CustomCroquetMessageDelegate(string selector, JSONObject data);
    public static CustomCroquetMessageDelegate customMessage = null;

    [System.Serializable]
    private class CroquetMessage
    {
        public string selector;
        public JSONObject data;
    }

    private Queue<CroquetMessage> croquetQueue = new Queue<CroquetMessage>();

    public virtual void Start()
    {
        theBridge = this;
        croquetReady = false; // suppress Update until we're ready

        Application.targetFrameRate = 60;

		Rect wvFrame = new Rect(0, 0, 0, 0);
		bool hideWV = true;
		// ------------------------------------------------------------------------------------
		// for debugging the Croquet code, replace the two lines above with the three below,
		// which will set up a visible WebView that supports right-click to get developer tools.
		// ------------------------------------------------------------------------------------
		//UniWebView.SetWebContentsDebuggingEnabled(true);
        //Rect wvFrame = new Rect(0, 0, 300, 300); // top left of screen, arbitrary size
        //bool hideWV = false;

        // Create a game object to hold UniWebView and add component.
        GameObject webViewGameObject = new GameObject("UniWebView");
        webView = webViewGameObject.AddComponent<UniWebView>();
        webView.OnMessageReceived += AsyncCroquetMessage;
        webView.OnPageFinished += (view, statusCode, url) =>
        {
            print("croqunity web view loaded: " + url + " with status " + statusCode);
        };
        webView.Frame = wvFrame;

		// use the following line if you're bundling and serving using "npm start"
		//string loadUrl = "http://localhost:9009/index.html";
		// use the following if you're bundling with "npm run build", then copying the
		// built index.html into the project's StreamingAssets folder.
		string loadUrl = UniWebViewHelper.StreamingAssetURLForPath("index.html");

		webView.Load(loadUrl);
        webView.Show();
        if (hideWV) webView.Hide(); // seems to help with not losing focus from the Unity window

        GameObject textObject = GameObject.Find("3D Debug Text");
        if (textObject != null) processOutput = textObject.GetComponent<TextMesh>();
    }

    public virtual void Update()
    {
        if (!croquetReady) return;

        // a convenient hook for telling Croquet to enter (or leave) some test mode
        if (Input.GetKeyDown("space"))
        {
            AnnounceTestTrigger();
        }

        // arrow keys move our avatar, if we've been set up with one
        if (avatarObject != null)
        {
            // both defined as change per ms
            float speed = 0.002f;
            float rotSpeed = 360f / 6000f;

            // NB: there can be a KeyDown and a KeyUp for the same key during the same frame.
            // we give priority to the KeyUp.

            newUpOrDown = upOrDown;
            if ((upOrDown == "up" && Input.GetKeyUp(KeyCode.UpArrow)) ||
                (upOrDown == "down" && Input.GetKeyUp(KeyCode.DownArrow))) newUpOrDown = "";
            else if (Input.GetKeyDown(KeyCode.UpArrow)) newUpOrDown = "up";
            else if (Input.GetKeyDown(KeyCode.DownArrow)) newUpOrDown = "down";

            if (newUpOrDown != upOrDown)
            {
                if (newUpOrDown == "") avatarObject.AvatarSetVelocity(null);
                else
                {
                    Vector3 forward = avatarObject.rotationTransform.localRotation * Vector3.forward * speed;
                    if (newUpOrDown == "up") avatarObject.AvatarSetVelocity(forward);
                    else avatarObject.AvatarSetVelocity(forward * -1f);
                }
                upOrDown = newUpOrDown;
            }

            newLeftOrRight = leftOrRight;
            if ((leftOrRight == "left" && Input.GetKeyUp(KeyCode.LeftArrow)) ||
                (leftOrRight == "right" && Input.GetKeyUp(KeyCode.RightArrow))) newLeftOrRight = "";
            else if (Input.GetKeyDown(KeyCode.LeftArrow)) newLeftOrRight = "left";
            else if (Input.GetKeyDown(KeyCode.RightArrow)) newLeftOrRight = "right";

            if (newLeftOrRight != leftOrRight)
            {
                // Unity uses left-handed axes!
                if (newLeftOrRight == "left") avatarObject.AvatarSetSpin(new Vector3(0f, 1f, 0f), -rotSpeed);
                else if (newLeftOrRight == "right") avatarObject.AvatarSetSpin(new Vector3(0f, 1f, 0f), rotSpeed);
                else avatarObject.AvatarSetSpin(null, 0f);
                leftOrRight = newLeftOrRight;
            }
        }

        ProcessCroquetQueue(); // process everything other than positional updates first
        UpdateAll((int)Math.Round(Time.deltaTime * 1000));

        // a convenient way of displaying debug text in the scene (if there is an assigned
        // processOutput text object, and if it's enabled).
        // to display something, just evaluate
        //   debugText = "something..."
        if (processOutput != null && !debugText.Equals(lastDebugText))
        {
            processOutput.text = debugText;
            lastDebugText = debugText;
        }
    }

    /*
    // FixedUpdate period is set in the Time window (Edit -> Project Settings -> Time)
    private void FixedUpdate()
    {
    }
    */

    // messages coming from Croquet through the data channel
    public void HandleCroquetMessage(string selector, JSONObject data)
    {
        try
        {
            // start with the highest-frequency message
            if (selector == "update_object") UpdateObject(data);
            else if (selector == "create_object") CreateObject(data); // synchronous creation; potentially delayed placement
            else if (selector == "set_sync_state") HandleSyncState(data);
            else if (selector == "set_debug_text") ShowDebugText(data);
            else QueueCroquetMessage(selector, data);
        }
        catch (Exception e)
        {
            Debug.Log("caught " + e.Message + " in " + data.ToString());
        }
    }

    // messages that we don't want to handle synchronously on arrival are put in a
    // queue to be processed during the next Update()
    private void QueueCroquetMessage(string selector, JSONObject data)
    {
        CroquetMessage msg = new CroquetMessage();
        msg.selector = selector;
        msg.data = data;
        croquetQueue.Enqueue(msg);
    }

    private void ProcessCroquetQueue()
    {
        while (croquetQueue.Count > 0)
        {
            CroquetMessage msg = croquetQueue.Dequeue();
            string selector = msg.selector;
            JSONObject data = msg.data;
            try
            {
                DispatchQueuedCroquetMessage(selector, data);
            }
            catch (Exception e)
            {
                Debug.Log("caught " + e.Message + " in " + data.ToString());
            }
        }
    }

    private void DispatchQueuedCroquetMessage(string selector, JSONObject data)
    {
        // if a customMessage delegate has been set, give it a chance to handle the
        // message.  it should return true if handled, else false.
        if (customMessage != null)
        {
            bool handled = customMessage(selector, data);
            if (handled) return;
        }

        if (selector == "add_child") AddChild(data);
        else if (selector == "delete_object") DeleteObject(data);
    }

    private void UpdateAll(int deltaT)
    {
        foreach (KeyValuePair<int, CroquetObject> entry in croquetObjects)
        {
            entry.Value.UpdateForTimeDelta(deltaT);
        }
    }

    public virtual void DeleteAll() // (messy: just this one public/virtual, so demo can override)
    {
        List<int> handleList = new List<int>(croquetObjects.Keys);
        foreach (int handle in handleList)
        {
            DeleteObjectByHandle(handle);
        }
    }

    public CroquetObject FindCroquetObject(JSONObject data, string handleProp)
    {
        int handle = (int)data.GetField(handleProp).i;
        if (croquetObjects.ContainsKey(handle)) return croquetObjects[handle];

        throw new Exception("croquet object " + handle + " not found");
    }

    private void CreateObject(JSONObject data)
    {
        int handle = (int)data.GetField("handle").i;

        if (croquetObjects.ContainsKey(handle))
        {
            // not meant to happen
            Debug.Log("REJECTING RE-CREATION OF CROQUET OBJECT");
            Debug.Log(data.ToString());
            return;
        }

        // each object created on behalf of Croquet is a GameObject with a CroquetObject component. 
        // the croquetObjects dictionary holds the CroquetObject instances.
        GameObject unityObject = null;
        CroquetObject croquetObject = null;
        Type croquetComponentType = null;

        // if a customCreate delegate has been set, give it a chance to create the GameObject
        // and/or set the CroquetObject component type.
        if (customCreate != null) customCreate(data, out unityObject, out croquetComponentType);

        string type = data.GetField("type").str;
        if (unityObject == null)
        {
            bool isEmpty = type == "userAvatar" || type == "empty";
            if (isEmpty)
            {
                unityObject = new GameObject();
            }
            else
            {
                unityObject = GameObject.CreatePrimitive(
                    type == "sphere" ? PrimitiveType.Sphere :
                    type == "cylinder" ? PrimitiveType.Cylinder :
                    PrimitiveType.Cube);

                // if the model requires any collisions, those must be computed by croquet (and not
                // the Unity physics engine) to ensure determinism.  but croquet objects need colliders
                // if we want to use raycasting to pick them.  in that case, instead of disabling
                // the collider we can put the object in the croquet layer (layer 8), which has
                // collisions with all other layers disabled (in Physics -> Layer Collision Matrix)

                //if (config.pickable) unityObject.layer = 8;
                //else unityObject.GetComponent<Collider>().enabled = false; // and leave in the default layer
                unityObject.GetComponent<Collider>().enabled = false;

                JSONObject[] hsvJSON = data.GetField("hsv").list.ToArray();
                float alpha = data.GetField("alpha").n;

                if (alpha == 0) unityObject.GetComponent<MeshRenderer>().enabled = false;
                else
                {
                    Material material = unityObject.GetComponent<Renderer>().material;
                    Color color = Color.HSVToRGB(hsvJSON[0].n / 360f, hsvJSON[1].n / 100f, hsvJSON[2].n / 100f);
                    if (alpha != 1.0f)
                    {
                        // sorcery from https://forum.unity.com/threads/standard-material-shader-ignoring-setfloat-property-_mode.344557/
                        material.SetOverrideTag("RenderType", "Transparent");
                        material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                        material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                        material.SetInt("_ZWrite", 0);
                        material.DisableKeyword("_ALPHATEST_ON");
                        material.DisableKeyword("_ALPHABLEND_ON");
                        material.EnableKeyword("_ALPHAPREMULTIPLY_ON");
                        material.renderQueue = 3000;

                        color.a = alpha;
                    }
                    material.color = color;
                }
            }
        }

        if (croquetComponentType == null) croquetComponentType = typeof(CroquetObject);
        croquetObject = (CroquetObject)unityObject.AddComponent(croquetComponentType);
        croquetObject.Initialize(data, unityObject);

        croquetObjects[handle] = croquetObject;

        // if an object of type "userAvatar" is created in a message that has a viewId property,
        // and the viewId equals the localViewId, the new object is the local user's userAvatar.
        if (type == "userAvatar" && data.GetField("viewId") && data.GetField("viewId").str == localViewId) avatarObject = croquetObject;

        //Debug.Log("created object " + handle);
    }

    private void AddChild(JSONObject data)
    {
        CroquetObject parent = FindCroquetObject(data, "h");
        CroquetObject child = FindCroquetObject(data, "childH");
        parent.AddChild(child);
    }

    private void UpdateObject(JSONObject data)
    {
        CroquetObject obj = FindCroquetObject(data, "h");
        obj.EnqueueUpdate(data);
    }

    private void DeleteObject(JSONObject data)
    {
        DeleteObjectByHandle((int)data.GetField("h").i);
    }
    private void DeleteObjectByHandle(int handle)
    {
        if (croquetObjects.ContainsKey(handle))
        {
            //Debug.Log("deleting object " + handle);
            croquetObjects[handle].Delete();
            croquetObjects.Remove(handle);
        }
        else Debug.Log("object not found for deletion: " + handle);
    }

    private void HandleSyncState(JSONObject data)
    {
        // if there is an object called "Sync Wait" in the scene, it will be
        // made visible while the Croquet client is syncing with the session.
        bool synced = data.GetField("synced").b;
        GameObject telltale = GameObject.Find("Sync Telltale");
        if (telltale != null) telltale.GetComponent<MeshRenderer>().enabled = !synced;
        //debugText = "synced: " + synced.ToString();
    }

    private void ShowDebugText(JSONObject data)
    {
        debugText = data.GetField("text").str;
    }

    public void SendMessage(string selector, List<object> dataArgs)
    {
        JSONObject message = new JSONObject(JSONObject.Type.OBJECT);
        message.AddField("selector", selector);
        JSONObject data = new JSONObject(JSONObject.Type.OBJECT);
        for (int i = 0; i < dataArgs.Count; i += 3)
        {
            string key = (string)dataArgs[i];
            string type = (string)dataArgs[i + 1];
            switch (type)
            {
                case "string":
                    data.AddField(key, (string)dataArgs[i + 2]);
                    break;
                case "int":
                    data.AddField(key, (int)dataArgs[i + 2]);
                    break;
                case "float":
                    data.AddField(key, (float)dataArgs[i + 2]);
                    break;
                default:
                    Debug.Log("unknown message-property type: " + type);
                    break;
            }
        }
        message.AddField("data", data);
        SendOnDataChannel(message.ToString());
    }

    public void SendSimpleMessage(string selector)
    {
        JSONObject message = new JSONObject(JSONObject.Type.OBJECT);
        message.AddField("selector", selector);
        SendOnDataChannel(message.ToString());
    }

    private void AnnounceTestTrigger()
    {
        SendSimpleMessage("test_trigger");
    }

    public void AnnounceAvatarVelocity(int handle, Vector3? velocity)
    {
        List<object> dataArgs = new List<object>();
        dataArgs.AddRange(new List<object>() { "h", "int", handle });
        if (velocity != null) // only add x, y, z if there is a velocity
        {
            Vector3 v = (Vector3)velocity;
            dataArgs.AddRange(new List<object>()
            {
                "x", "float", v.x,
                "y", "float", v.y,
                "z", "float", v.z
            });
        }
        SendMessage("avatar_velocity", dataArgs);
    }

    public void AnnounceAvatarSpin(int handle, Vector3? axis, float degreesPerMS)
    {
        List<object> dataArgs = new List<object>();
        dataArgs.AddRange(new List<object>() { "h", "int", handle });
        if (axis != null) // only add x, y, z if there is some spin
        {
            Vector3 v = (Vector3)axis;
            dataArgs.AddRange(new List<object>()
            {
                "x", "float", v.x,
                "y", "float", v.y,
                "z", "float", v.z,
                "rate", "float", degreesPerMS * Mathf.PI / 180f
            });
        }
        SendMessage("avatar_spin", dataArgs);
    }


    // ----------------------------------
    // setting up connection with Croquet
    // ----------------------------------

    private void SendOnSideChannel(string msg)
    {
        //Debug.Log(msg);
        webView.EvaluateJavaScript("unitySideChannelMessage(`" + msg + "`)");
    }

    private void AsyncCroquetMessage(UniWebView view, UniWebViewMessage message)
    {
        // a single message is encoded as a selector plus a JSON-stringified data object:
        // croquetMessage?selector=hello&data=<...JSON...>
        string selector = message.Args["selector"];
        string argString = "";
        JSONObject data = null;
        if (message.Args.ContainsKey("data"))
        {
            argString = message.Args["data"];
            data = new JSONObject(Uri.UnescapeDataString(argString));
        }
        //Debug.Log("async: " + message.Args["selector"] + ", " + argString);

        if (selector == "URM_ready") InitializeForClient(data);
        else if (selector == "rtc_setup_answer") dataChannel.AsyncReceiveAnswer(data);
    }

    // this will be triggered whenever the Croquet page loads or reloads.
    // if it's a reload, we need to clear the decks for whatever objects the
    // client is going to want to set up this time.
    private void InitializeForClient(JSONObject data)
    {
        croquetReady = false; // hold off on Update until we're ready to go [again]

        DeleteAll();
        if (dataChannel != null) dataChannel.Shutdown();

        avatarObject = null;
        upOrDown = "";
        newUpOrDown = "";
        leftOrRight = "";
        newLeftOrRight = "";

        GameObject telltale = GameObject.Find("Sync Telltale");
        if (telltale != null) telltale.GetComponent<MeshRenderer>().enabled = true;

        localViewId = data.GetField("viewId").str;
        Debug.Log("local viewId: " + localViewId);
        StartDataChannel();
    }

    private void StartDataChannel()
    {
        GameObject dataChannelObj = GameObject.Find("Data Channel");
        dataChannel = dataChannelObj.GetComponent<CroquetDataChannel>();
        dataChannel.SetCroquetBridge(this);
    }

    public void SendRTCIceCandidate(RTCIceCandidate iceCandidate)
    {
        JSONObject message = new JSONObject(JSONObject.Type.OBJECT);
        message.AddField("selector", "rtc_setup_ice_candidate");
        JSONObject data = new JSONObject(JSONObject.Type.OBJECT);
        data.AddField("candidate", iceCandidate.candidate);
        data.AddField("sdpMid", iceCandidate.sdpMid);
        data.AddField("sdpMLineIndex", iceCandidate.sdpMLineIndex);
        message.AddField("data", data);
        SendOnSideChannel(message.ToString());
    }

    public void SendRTCOffer(string offer)
    {
        JSONObject message = new JSONObject(JSONObject.Type.OBJECT);
        message.AddField("selector", "rtc_setup_offer");
        JSONObject data = new JSONObject(JSONObject.Type.OBJECT);
        data.AddField("offer", Regex.Replace(offer, @"\r\n?|\n", "_+_")); // https://stackoverflow.com/questions/238002/replace-line-breaks-in-a-string-c-sharp
        message.AddField("data", data);
        SendOnSideChannel(message.ToString());
    }

    public void RTCReady()
    {
        SendSimpleMessage("unity_ready");

        croquetReady = true;
    }

    public void SendOnDataChannel(string msg)
    {
        dataChannel.SendOnDataChannel(msg);
    }

    private void OnApplicationQuit()
    {
        dataChannel.Shutdown();
        UnityEngine.Debug.Log("Application ending after " + Time.time + " seconds");
    }
}
