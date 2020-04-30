using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CroquetBridgeTest : CroquetBridge
{
    public override void Start()
    {
        // for this demo, we want to set the CroquetObject type of an avatar
        // object to our custom class CroquetObjectTest, which has the AttachCamera
        // behaviour.
        customCreate = (JSONObject data, out GameObject unityObject, out Type croquetComponentType) =>
        {
            unityObject = null;
            croquetComponentType = null;

            string type = data.GetField("type").str;
            if (type == "avatar") croquetComponentType = typeof(CroquetObjectTest);
        };

        // when an attach_camera message comes through from Croquet, dispatch it to
        // our custom handler.
        customMessage = (string selector, JSONObject data) =>
        {
            if (selector == "attach_camera")
            {
                AttachCameraToObject(data);
                return true; // meaning that this message has been handled
            }

            return false;
        };

        base.Start();
    }

    public override void Update()
    {
        if (!croquetReady) return;

        // detect the C key press that signals wanting to capture the camera
        if (avatarObject != null && Input.GetKeyDown("c")) AnnounceCameraCapture(avatarObject.handle);

        base.Update();
    }

    // as part of deleting all objects, make sure to reset the possibly-captured camera
    public override void DeleteAll()
    {
        ResetCamera();
        base.DeleteAll();
    }

    private void AnnounceCameraCapture(int handle)
    {
        SendMessage("capture_camera", new List<object>() {
            "h", "int", handle
            });
    }

    private void AttachCameraToObject(JSONObject data)
    {
        if (data.GetField("h").type == JSONObject.Type.NULL) ResetCamera();
        else ((CroquetObjectTest)FindCroquetObject(data, "h")).AttachCamera();
    }

    public void ResetCamera()
    {
        GameObject camera = GameObject.Find("Main Camera");
        camera.transform.SetParent(null);
        camera.transform.localPosition = new Vector3(0, 1, -10);
        camera.transform.localRotation = Quaternion.identity;
    }
}
