using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.WebRTC;

public class CroquetDataChannel : MonoBehaviour
{
    private CroquetBridge croquetBridge;

    private RTCPeerConnection pc = null;
    private RTCDataChannel dataChannel = null;
    private Coroutine sdpCheck;
    private string msg;
    private DelegateOnIceConnectionChange pcOnIceConnectionChange;
    private DelegateOnIceCandidate pcOnIceCandidate;
    private DelegateOnMessage onDataChannelMessage;
    private DelegateOnOpen onDataChannelOpen;
    private DelegateOnClose onDataChannelClose;
    //private DelegateOnDataChannel onDataChannel;

    private RTCOfferOptions OfferOptions = new RTCOfferOptions
    {
        iceRestart = false,
        offerToReceiveAudio = false,
        offerToReceiveVideo = false
    };

    private RTCAnswerOptions AnswerOptions = new RTCAnswerOptions
    {
        iceRestart = false,
    };

    private void Awake()
    {
        WebRTC.Initialize();
    }

    private void OnDestroy()
    {
        // we do the cleanup in Shutdown
        WebRTC.Finalize();
    }

    public void Shutdown()
    {
        dataChannel.Close();
        pc.Close();
        //WebRTC.Finalize();
    }

    public void SetCroquetBridge(CroquetBridge b)
    {
        Debug.Log("SetCroquetBridge");
        croquetBridge = b;
        StartCoroutine("Connect");
    }

    private void Start()
    {
        Debug.Log("starting DataChannel");
        pcOnIceConnectionChange = new DelegateOnIceConnectionChange(state => { OnIceConnectionChange(state); });
        pcOnIceCandidate = new DelegateOnIceCandidate(candidate => { OnIceCandidate(candidate); });
        onDataChannelMessage = new DelegateOnMessage(bytes => { OnDataChannelMessage(System.Text.Encoding.UTF8.GetString(bytes)); });
        onDataChannelOpen = new DelegateOnOpen(() => { OnDataChannelOpen(); });
        onDataChannelClose = new DelegateOnClose(() => { Debug.Log("CLOSE"); });
    }

    RTCConfiguration GetSelectedSdpSemantics()
    {
        RTCConfiguration config = default;
        config.iceServers = new RTCIceServer[]
        {
            new RTCIceServer { urls = new string[] { "stun:stun.l.google.com:19302" } }
        };

        return config;
    }
    void OnIceConnectionChange(RTCIceConnectionState state)
    {
        switch (state)
        {
            case RTCIceConnectionState.New:
                Debug.Log("IceConnectionState: New");
                break;
            case RTCIceConnectionState.Checking:
                Debug.Log("IceConnectionState: Checking");
                break;
            case RTCIceConnectionState.Closed:
                Debug.Log("IceConnectionState: Closed");
                break;
            case RTCIceConnectionState.Completed:
                Debug.Log("IceConnectionState: Completed");
                break;
            case RTCIceConnectionState.Connected:
                Debug.Log("IceConnectionState: Connected");
                break;
            case RTCIceConnectionState.Disconnected:
                Debug.Log("IceConnectionState: Disconnected");
                break;
            case RTCIceConnectionState.Failed:
                Debug.Log("IceConnectionState: Failed");
                break;
            case RTCIceConnectionState.Max:
                Debug.Log("IceConnectionState: Max");
                break;
            default:
                break;
        }
    }

    void OnDataChannelOpen()
    {
        Debug.Log("data channel open");
        dataChannel.OnMessage = onDataChannelMessage;
        croquetBridge.RTCReady(); // will run the clock probe, then tell croquet we're ready
    }

    public void SendOnDataChannel(string message)
    {
        dataChannel.Send(message);
    }

    void OnDataChannelMessage(string eventsJSON)
    {
        string[] events = eventsJSON.Split(
                    new[] { System.Environment.NewLine },
                    StringSplitOptions.None);
        for (int i = 0; i < events.Length; i += 2)
        {
            string selector = events[i];
            JSONObject data = new JSONObject(events[i + 1]);

            croquetBridge.HandleCroquetMessage(selector, data);
        }
    }

    IEnumerator Connect()
    {
        Debug.Log("starting to connect");
        var configuration = GetSelectedSdpSemantics();
        pc = new RTCPeerConnection(ref configuration);
        pc.OnIceCandidate = pcOnIceCandidate;
        pc.OnIceConnectionChange = pcOnIceConnectionChange;
        //pc.OnDataChannel = onDataChannel;

        RTCDataChannelInit conf = new RTCDataChannelInit(true); // true => reliable
        dataChannel = pc.CreateDataChannel("data", ref conf);
        dataChannel.OnOpen = onDataChannelOpen;

        var op = pc.CreateOffer(ref OfferOptions);
        yield return op; // yield until async CreateOffer returns

        if (!op.isError)
        {
            yield return StartCoroutine(OnCreateOfferSuccess(op.desc));
        }
        else
        {
            Debug.Log("createOffer error");
            Debug.Log(op.error);
        }
    }

    void OnIceCandidate(RTCIceCandidate​ candidate)
    {
        Debug.Log("OnIceCandidate " + candidate.candidate);
        croquetBridge.SendRTCIceCandidate(candidate);
    }

    IEnumerator OnCreateOfferSuccess(RTCSessionDescription desc)
    {
        Debug.Log($"Offer from here\n{desc.sdp}");
        var op = pc.SetLocalDescription(ref desc);
        yield return op; // yield until SetLocalDescription returns

        if (op.isError)
        {
            Debug.Log("setLocalDescription error");
            Debug.Log(op.error);
            yield break;
        }

        croquetBridge.SendRTCOffer(desc.sdp);
    }

    public void AsyncReceiveAnswer(JSONObject data)
    {
        StartCoroutine(ReceiveAnswer(data));
    }
    private IEnumerator ReceiveAnswer(JSONObject data) {
        //Debug.Log("ReceiveAnswer");
        Debug.Log(data);
        RTCSessionDescription desc = new RTCSessionDescription();
        desc.sdp = data.GetField("answer").str.Replace("_+_", System.Environment.NewLine);
        desc.type = RTCSdpType.Answer;

        var op = pc.SetRemoteDescription(ref desc);
        yield return op;
        if (op.isError)
        {
            Debug.Log("setRemoteDescription error");
            Debug.Log(op.error);
        }
    }
}
