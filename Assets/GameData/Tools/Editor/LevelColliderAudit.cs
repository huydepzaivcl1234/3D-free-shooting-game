using MiningSimulator.Ores;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

internal static class LevelColliderAudit
{
    private const string MenuPath = "Tools/Audit Level Colliders";
    private const string MaterialPath = "Assets/GameData/ZeroFriction.physicMaterial";

    [MenuItem(MenuPath, true)]
    private static bool CanAudit() => !EditorApplication.isPlayingOrWillChangePlaymode;

    [MenuItem(MenuPath)]
    private static void Audit()
    {
        PhysicsMaterial zeroFriction = null;
        int audited = 0, assigned = 0, warnings = 0;
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Audit Level Colliders");

        for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
            if (!scene.isLoaded || EditorSceneManager.IsPreviewScene(scene)) continue;
            foreach (var root in scene.GetRootGameObjects())
            foreach (var collider in root.GetComponentsInChildren<Collider>(true))
            {
                if (!IsEnvironment(collider, root.name == "Map")) continue;
                audited++;
                if (collider is MeshCollider meshCollider && LooksLikeBoxWall(meshCollider))
                {
                    warnings++;
                    Debug.LogWarning("Straight wall uses a MeshCollider. Consider using a BoxCollider fitted to the wall.", collider);
                }

                if (collider.sharedMaterial) continue;
                if (!zeroFriction) zeroFriction = LoadOrCreateMaterial();
                if (!zeroFriction) continue;
                Undo.RecordObject(collider, "Assign ZeroFriction");
                collider.sharedMaterial = zeroFriction;
                PrefabUtility.RecordPrefabInstancePropertyModifications(collider);
                EditorSceneManager.MarkSceneDirty(scene);
                assigned++;
            }
        }

        Undo.CollapseUndoOperations(group);
        Debug.Log($"Level collider audit: {audited} checked, {assigned} assigned ZeroFriction, {warnings} wall warnings.");
    }

    private static bool IsEnvironment(Collider collider, bool inMap)
    {
        if (collider.isTrigger || collider.attachedRigidbody || collider is CharacterController ||
            collider.GetComponentInParent<CharacterHealth>(true)) return false;
        return collider is TerrainCollider || GroundSurfaceLayers.Contains(collider.gameObject.layer) ||
            collider.GetComponent("ProBuilderMesh") ||
            ((inMap || collider.gameObject.isStatic) && collider is MeshCollider or BoxCollider &&
             collider.GetComponentInParent<Renderer>(true));
    }

    private static PhysicsMaterial LoadOrCreateMaterial()
    {
        var existing = AssetDatabase.LoadMainAssetAtPath(MaterialPath);
        if (existing is PhysicsMaterial material) return material;
        if (existing)
        {
            Debug.LogError($"Cannot create ZeroFriction because {MaterialPath} is already used.");
            return null;
        }

        material = new PhysicsMaterial("ZeroFriction")
        {
            staticFriction = 0,
            dynamicFriction = 0,
            bounciness = 0,
            frictionCombine = PhysicsMaterialCombine.Minimum,
            bounceCombine = PhysicsMaterialCombine.Minimum
        };
        AssetDatabase.CreateAsset(material, MaterialPath);
        return material;
    }

    private static bool LooksLikeBoxWall(MeshCollider collider)
    {
        Mesh mesh = collider.sharedMesh;
        if (!mesh || !mesh.isReadable || Vector3.Dot(collider.transform.up, Vector3.up) < .99f) return false;
        Vector3 size = Vector3.Scale(mesh.bounds.size, collider.transform.lossyScale);
        float thickness = Mathf.Min(Mathf.Abs(size.x), Mathf.Abs(size.z));
        if (Mathf.Abs(size.y) < .75f || thickness > Mathf.Abs(size.y) * .5f) return false;

        Bounds bounds = mesh.bounds;
        const float tolerance = .001f;
        int faceMask = 0;
        Vector3[] vertices = mesh.vertices;
        int[] triangles = mesh.triangles;
        for (int i = 0; i < triangles.Length; i += 3)
        {
            Vector3 a = vertices[triangles[i]];
            Vector3 b = vertices[triangles[i + 1]];
            Vector3 c = vertices[triangles[i + 2]];
            int triangleFaces = 0;
            for (int axis = 0; axis < 3; axis++)
            {
                if (OnPlane(a[axis], b[axis], c[axis], bounds.min[axis], tolerance))
                    triangleFaces |= 1 << (axis * 2);
                if (OnPlane(a[axis], b[axis], c[axis], bounds.max[axis], tolerance))
                    triangleFaces |= 1 << (axis * 2 + 1);
            }
            if (triangleFaces == 0) return false;
            faceMask |= triangleFaces;
        }
        return faceMask == 63;
    }

    private static bool OnPlane(float a, float b, float c, float plane, float tolerance) =>
        Mathf.Abs(a - plane) < tolerance && Mathf.Abs(b - plane) < tolerance && Mathf.Abs(c - plane) < tolerance;
}
