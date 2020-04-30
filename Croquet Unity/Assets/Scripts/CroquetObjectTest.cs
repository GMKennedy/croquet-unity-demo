using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;

public class CroquetObjectTest : CroquetObject
{
    public void AttachCamera()
    {
        GameObject camera = GameObject.Find("Main Camera");
        camera.transform.SetParent(parentableObject.transform);
        camera.transform.localPosition = new Vector3();
        camera.transform.localRotation = Quaternion.identity;
    }
}

