using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;

public class CroquetObject : MonoBehaviour
{
    public int handle;
    private bool isEmpty;
    private bool isAvatar;
    public GameObject unityObject;
    public GameObject parentableObject;
    public Transform positionTransform;
    public Transform rotationTransform;
    public Transform scaleTransform;
    private Vector3? targetPosition = null;
    private Quaternion? targetRotation = null;
    private Vector3? targetScale = null;
    private Vector3? avatarVelocity = null; // units per ms
    private Vector3? avatarSpinAxis = null;
    private float avatarSpinRate = 0; // degrees per ms

    private static float valScale = 10000f; // factor used in sending vectors & quaternions as stringified integers

    public void Initialize(JSONObject data, GameObject uObj)
    {
        unityObject = uObj;
        handle = (int)data.GetField("handle").i;
        string type = data.GetField("type").str;
        isAvatar = type == "avatar";
        isEmpty = isAvatar || type == "empty";

        if (isEmpty)
        {
            parentableObject = unityObject;
            positionTransform = rotationTransform = scaleTransform = unityObject.transform;
        }
        else
        {
            // visible (non-empty) unity objects are given a parent that is an unscaled
            // empty object.
            // position and rotation are applied to the parent.  scale is applied only to the
            // inner object.
            parentableObject = new GameObject();
            unityObject.transform.SetParent(parentableObject.transform);
            positionTransform = rotationTransform = parentableObject.transform;
            scaleTransform = unityObject.transform;
        }

        unityObject.SetActive(false); // don't render until it's been updated (i.e., placed)
        EnqueueUpdate(data);
    }

    public void Delete()
    {
        //Debug.Log("delete Croquet Object " + handle);
        if (parentableObject != null) Destroy(parentableObject);
        // that might be it.  this object (the CroquetObject) is a component of
        // parentableObject, or maybe of its child.  either way, Unity's going to
        // dispose of everything below the PO.
    }

    [System.Serializable]
    public class UpdateSpec
    {
        public string type; // "set" or "to"
        public string stringVal; // usually representing an integer-scaled array
    }

    private Queue<UpdateSpec> positionQueue = new Queue<UpdateSpec>();
    private Queue<UpdateSpec> rotationQueue = new Queue<UpdateSpec>();
    private Queue<UpdateSpec> scaleQueue = new Queue<UpdateSpec>();

    private void AddUpdateToQueue(Queue<UpdateSpec> queue, string type, string val)
    {
        UpdateSpec spec = new UpdateSpec();
        spec.type = type;
        spec.stringVal = val;
        queue.Enqueue(spec);
    }

    public void EnqueueUpdate(JSONObject updates)
    {
        if (updates.GetField("setP") != null)
            AddUpdateToQueue(positionQueue, "set", updates.GetField("setP").str);
        else if (updates.GetField("toP") != null)
            AddUpdateToQueue(positionQueue, "to", updates.GetField("toP").str);

        if (updates.GetField("setR") != null)
            AddUpdateToQueue(rotationQueue, "set", updates.GetField("setR").str);
        else if (updates.GetField("toR") != null)
            AddUpdateToQueue(rotationQueue, "to", updates.GetField("toR").str);

        if (updates.GetField("setS") != null)
            AddUpdateToQueue(scaleQueue, "set", updates.GetField("setS").str);
        else if (updates.GetField("toS") != null)
            AddUpdateToQueue(scaleQueue, "to", updates.GetField("toS").str);
    }

    private string[] AnalyzeQueue(Queue<UpdateSpec> queue)
    {
        // return a two-element string containing the values for "set" and "to" updates, if any
        string[] result = new string[2] { "", "" };
        UpdateSpec[] array = queue.ToArray();
        queue.Clear();
        int i = array.Length - 1;
        bool keepLooking = true;
        while (i >= 0 && keepLooking)
        {
            UpdateSpec spec = array[i];
            if (spec.type == "set")
            {
                result[0] = spec.stringVal;
                keepLooking = false;
            } else
            {
                // it's a "to".  return only the latest (i.e., the first encountered),
                // but keep looking in case there's a preceding "set"
                if (result[1] == "") result[1] = spec.stringVal;
            }
            i--;
        }

        return result;
    }
    private Vector3 StringToVector(string val)
    {
        string[] nums = val.Split(',');
        return new Vector3(float.Parse(nums[0]) / valScale, float.Parse(nums[1]) / valScale, float.Parse(nums[2]) / valScale);
    }
    private Quaternion StringToQuaternion(string val)
    {
        string[] nums = val.Split(',');
        return new Quaternion(float.Parse(nums[0]) / valScale, float.Parse(nums[1]) / valScale, float.Parse(nums[2]) / valScale, float.Parse(nums[3]) / valScale);
    }
    public void UpdateForTimeDelta(int deltaT)
    {
        float tug = isAvatar ? 0.05f : 0.2f;
        float scaledTug = Mathf.Min(1f, tug * (float)deltaT / 17f);

        string[] setAndTo;
        string setVal, toVal;
        if (positionQueue.Count > 0)
        {
            setAndTo = AnalyzeQueue(positionQueue);
            setVal = setAndTo[0];
            toVal = setAndTo[1];
            if (setVal != "") positionTransform.localPosition = StringToVector(setVal);
            if (toVal != "") targetPosition = StringToVector(toVal);
            else if (setVal != "") targetPosition = null; // cancel "to" if there was a "set"
        }
        if (targetPosition != null)
        {
            positionTransform.localPosition = Vector3.Lerp(positionTransform.localPosition, (Vector3)targetPosition, scaledTug);
            if (positionTransform.localPosition == targetPosition) targetPosition = null;
        }

        if (rotationQueue.Count > 0)
        {
            setAndTo = AnalyzeQueue(rotationQueue);
            setVal = setAndTo[0];
            toVal = setAndTo[1];
            if (setVal != "") rotationTransform.localRotation = StringToQuaternion(setVal);
            if (toVal != "") targetRotation = StringToQuaternion(toVal);
            else if (setVal != "") targetRotation = null;
        }
        if (targetRotation != null)
        {
            rotationTransform.localRotation = Quaternion.Lerp(rotationTransform.localRotation, (Quaternion)targetRotation, scaledTug);
            if (rotationTransform.localRotation == targetRotation) targetRotation = null;
        }

        if (scaleQueue.Count > 0)
        {
            setAndTo = AnalyzeQueue(scaleQueue);
            setVal = setAndTo[0];
            toVal = setAndTo[1];
            if (setVal != "") scaleTransform.localScale = StringToVector(setVal);
            if (toVal != "") targetScale = StringToVector(toVal);
            else if (setVal != "") targetScale = null;
        }
        if (targetScale != null)
        {
            scaleTransform.localScale = Vector3.Lerp(scaleTransform.localScale, (Vector3)targetScale, scaledTug);
            if (scaleTransform.localScale == targetScale) targetScale = null;
        }

        // now apply avatar velocity and spin
        if (avatarVelocity != null)
        {
            positionTransform.localPosition += ((Vector3)avatarVelocity) * deltaT;
        }

        if (avatarSpinAxis != null)
        {
            rotationTransform.localRotation *= Quaternion.AngleAxis(avatarSpinRate * deltaT, (Vector3)avatarSpinAxis);
        }

        if (!unityObject.activeSelf) unityObject.SetActive(true); // activate on first update
    }

    // helper function for setting opacity of this object's children, depending on
    // whether the user is currently steering it
    private void SetAvatarOpacity()
    {
        float opacity = (avatarVelocity == null && avatarSpinAxis == null) ? 0.5f : 1f;
        Transform transform = parentableObject.transform;
        // first level of children will be the [transforms of the] parentableObjects
        // of the actual child GOs
        for (int i = 0; i < transform.childCount; i++)
        {
            GameObject childPO = transform.GetChild(i).gameObject;
            for (int j = 0; j < childPO.transform.childCount; j++)
            {
                GameObject realChild = childPO.transform.GetChild(j).gameObject;
                Material material = realChild.GetComponent<Renderer>().material;
                Color color = material.color;
                color.a = opacity;
                material.color = color;
            }
        }
    }
    public void AvatarSetVelocity(Vector3? changePerMS)
    {
        avatarVelocity = changePerMS;
        SetAvatarOpacity();
        CroquetBridge.theBridge.AnnounceAvatarVelocity(handle, changePerMS);
    }

    public void AvatarSetSpin(Vector3? axis, float degreesPerMS)
    {
        avatarSpinAxis = axis;
        avatarSpinRate = degreesPerMS;
        SetAvatarOpacity();
        CroquetBridge.theBridge.AnnounceAvatarSpin(handle, axis, degreesPerMS);
    }

    public void AddChild(CroquetObject childObj)
    {
        // the parent/child relationship is between the (unscaled) object holders
        Transform parentTransform = parentableObject.transform;
        Transform childTransform = childObj.parentableObject.transform;
        childTransform.SetParent(parentTransform); //, false); ...false if we want child to ignore prior world position
    }
}

