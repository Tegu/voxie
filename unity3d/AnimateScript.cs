﻿/*
Copyright (c) 2013 Mathias Kaerlev

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
*/

// Animation script.
// Use tools/convert.py to create meta .bytes files and .dae files.
// Place the .bytes files in the Resources/Meta directory to enable loading
// of voxel metadata.

using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.IO;

[System.Serializable]
public class AnimationType
{
    public enum LoopType
    {
        Forward = 0,
        PingPong = 1
    }

    public string name;
    public List<GameObject> models = new List<GameObject>();
    public float interval;
    public int loops = -1;
    public LoopType type = LoopType.Forward;
}

public class VoxelData
{
    public uint x_size, y_size, z_size;
    public int x_offset, y_offset, z_offset;
    public byte[] data;
    public Material[] palette;

    public VoxelData(string name, Material[] materials)
    {
        TextAsset asset = (TextAsset)Resources.Load("Meta/" + name);
        MemoryStream ms = new MemoryStream(asset.bytes);
        BinaryReader reader = new BinaryReader(ms);
        x_size = reader.ReadUInt32();
        z_size = reader.ReadUInt32();
        y_size = reader.ReadUInt32();
        x_offset = reader.ReadInt32();
        z_offset = reader.ReadInt32();
        y_offset = reader.ReadInt32();
        data = reader.ReadBytes((int)(x_size * y_size * z_size));
        byte[] raw_palette = reader.ReadBytes(256*3);
        palette = new Material[256];
        for (int i = 0; i < 256; i++) {
            float r = raw_palette[i*3] / 255.0f;
            float g = raw_palette[i*3+1] / 255.0f;
            float b = raw_palette[i*3+2] / 255.0f;
            Material result = null;
            foreach (Material mat in materials) {
                if (mat.color.r == r && mat.color.g == g && mat.color.b == b) {
                    result = mat;
                    break;
                }
            }
            palette[i] = result;
        }
    }

    bool is_solid(int x, int y, int z)
    {
        if (x < 0 || x >= x_size || y < 0 || x >= y_size ||
            z < 0 || z >= z_size)
            return false;
        byte c = data[y + z * y_size + x * y_size * z_size];
        return c != 255;
    }

    public bool is_surface(int x, int y, int z)
    {
        return !(is_solid(x - 1, y, z) && is_solid(x + 1, y, z) &&
                 is_solid(x, y - 1, z) && is_solid(x, y + 1, z) &&
                 is_solid(x, y, z - 1) && is_solid(x, y, z + 1));
    }

    public Material get(int x, int y, int z)
    {
        byte c = data[y + z * y_size + x * y_size * z_size];
        if (c == 255)
            return null;
        return palette[c];
    }
}

[ExecuteInEditMode]
public class AnimateScript : MonoBehaviour
{
    bool initialized = false;
    public List<AnimationType> animations;
    Dictionary<string, AnimationType> anim_instances
        = new Dictionary<string, AnimationType>();
    AnimationType anim = null;
    float time_value = 0.0f;
    static Dictionary<string, VoxelData> voxels
        = new Dictionary<string, VoxelData>();
    [System.NonSerialized]
    public string anim_name;
    [System.NonSerialized]
    public int animation_index = 0;
    [System.NonSerialized]
    public GameObject current = null;
    int loops = -1;
    bool reversed = false;

    void OnDisable()
    {
        if (current == null)
            return;
        current.SetActive(false);
    }

    void OnEnable()
    {
        if (current == null)
            return;
        current.SetActive(true);
    }

	void initialize()
    {
        if (initialized)
            return;
        initialized = true;
        Vector3 pos = transform.position;
        foreach (AnimationType anim_data in animations) {
            AnimationType new_anim = new AnimationType();
            new_anim.interval = anim_data.interval;
            new_anim.loops = anim_data.loops;
            new_anim.type = anim_data.type;
            anim_instances[anim_data.name] = new_anim;
            foreach (GameObject obj in anim_data.models) {
                GameObject new_obj = (GameObject)Instantiate(obj, pos,
                    Quaternion.identity);
                new_obj.name = obj.name;
                Vector3 scale = new_obj.transform.localScale;
                new_obj.transform.parent = gameObject.transform;
                new_obj.transform.localScale = scale;
                new_anim.models.Add(new_obj);
                new_obj.SetActive(false);

                // cache meta
                get_meta(new_obj);
            }
        }
	}

    public VoxelData get_meta()
    {
        return get_meta(current);
    }

    public VoxelData get_meta(GameObject model)
    {
        VoxelData item;
        if(!voxels.TryGetValue(model.name, out item)) {
            item = new VoxelData(model.name, model.renderer.sharedMaterials);
            voxels[model.name] = item;
        }
        return item;
    }

    void on_loop(int new_index)
    {
        if (loops != -1) {
            loops--;
            if (loops == 0)
                return;
        }

        switch (anim.type) {
            case AnimationType.LoopType.Forward:
                new_index = 0;
                break;
            case AnimationType.LoopType.PingPong:
                reversed = !reversed;
                if (reversed)
                    new_index = anim.models.Count - 2;
                else
                    new_index = 1;
                break;
        }

        animation_index = new_index;

    }

    void update_animation()
    {
        if (anim.interval == 0.0f)
            return;
        if (loops == 0)
            return;
        time_value += Time.deltaTime;
        while (time_value > anim.interval) {
            time_value -= anim.interval;
            anim.models[animation_index].SetActive(false);
            int new_index = animation_index;
            if (reversed)
                new_index--;
            else
                new_index++;
            if (new_index >= anim.models.Count || new_index < 0)
                on_loop(new_index);
            else
                animation_index = new_index;
            current = anim.models[animation_index];
            current.SetActive(true);
        }
    }

    void update_editor()
    {
        GameObject obj = animations[0].models[0];
        MeshFilter filter = obj.GetComponent<MeshFilter>();
        Mesh mesh = filter.sharedMesh;
        for (int i = 0; i < mesh.subMeshCount; i++) {
            Material mat = filter.renderer.sharedMaterials[i];
            Graphics.DrawMesh(mesh, transform.localToWorldMatrix, mat, 0, null,
                              i);
        }
    }

    void Update()
    {
        if (Application.isPlaying)
            update_animation();
        else
            update_editor();
    }

    public void set(string name)
    {
        if (!enabled)
            return;
        initialize();
        AnimationType new_anim = anim_instances[name];
        if (anim == new_anim)
            return;
        if (anim != null)
            anim.models[animation_index].SetActive(false);
        anim = new_anim;
        anim_name = name;
        loops = anim.loops;
        animation_index = 0;
        time_value = 0.0f;
        reversed = false;
        current = new_anim.models[animation_index];
        current.SetActive(true);
    }
}
