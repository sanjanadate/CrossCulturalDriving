﻿//======= Copyright (c) Stereolabs Corporation, All rights reserved. ===============

using UnityEngine;

public class RigBone {
    private readonly Animator animator;
    public HumanBodyBones bone;
    public GameObject gameObject;
    public bool isValid;
    private Quaternion savedValue;

    public RigBone(GameObject g, HumanBodyBones b) {
        gameObject = g;
        bone = b;
        isValid = false;
        animator = gameObject.GetComponent<Animator>();


        if (animator == null) {
            Debug.Log("no Animator Component");
            return;
        }

        var avatar = animator.avatar;
        if (avatar == null || !avatar.isHuman || !avatar.isValid) {
            Debug.Log("Avatar is not Humanoid or it is not valid");
            return;
        }

        if (!animator.GetBoneTransform(bone)) Debug.Log("Bone : " + bone + " not found ! ");

        isValid = true;
        savedValue = animator.GetBoneTransform(bone).localRotation;
    }

    public Transform transform => animator.GetBoneTransform(bone);

    public void set(float a, float x, float y, float z) {
        set(Quaternion.AngleAxis(a, new Vector3(x, y, z)));
    }

    public void set(Quaternion q) {
        animator.GetBoneTransform(bone).localRotation = q;
        savedValue = q;
    }

    public void mul(float a, float x, float y, float z) {
        mul(Quaternion.AngleAxis(a, new Vector3(x, y, z)));
    }

    public void mul(Quaternion q) {
        var tr = animator.GetBoneTransform(bone);
        tr.localRotation = q * tr.localRotation;
    }

    public void offset(float a, float x, float y, float z) {
        offset(Quaternion.AngleAxis(a, new Vector3(x, y, z)));
    }

    public void offset(Quaternion q) {
        animator.GetBoneTransform(bone).localRotation = q * savedValue;
    }

    public void changeBone(HumanBodyBones b) {
        bone = b;
        savedValue = animator.GetBoneTransform(bone).localRotation;
    }
}