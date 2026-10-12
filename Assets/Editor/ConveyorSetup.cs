using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class ConveyorSetup
{
    const string ModelPath="Assets/Models/belt conveyors.fbx";
    static ConveyorSetup() { EditorApplication.delayCall+=()=>{Inspect();if(File.Exists("ConveyorInstall.request"))Install();}; }
    [MenuItem("Tools/EPIC/Install Feed Conveyor")]
    public static void Install()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)return;
        var cycle=Object.FindFirstObjectByType<RobotMachiningCycle>();
        var asset=AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if(!cycle || !asset || cycle.conveyor)return;
        Vector3 pickup=cycle.raw.position;
        Vector3 direction=cycle.machinePosition-pickup;direction.y=0;direction.Normalize();
        RobotGrabController.ProjectionRange(cycle.raw.transform,Vector3.zero,Vector3.up,out float surfaceHeight,out _);
        var root=new GameObject("Raw Feed Conveyor");
        var control=root.AddComponent<ConveyorFeedController>();
        var model=Object.Instantiate(asset,root.transform);model.name="Imported Conveyor Module";
        foreach(Transform t in model.transform)
            if(t.name!="05-10274069-DE1_Simplify_1" && t.name!="05-10029156-DE1_Shrinkwrap_1" && t.name!="05-10029156-DE1_Shrinkwrap_1.001")Object.DestroyImmediate(t.gameObject);
        var nodes=model.GetComponentsInChildren<Transform>();
        var endSupport=nodes.First(t=>t.name=="05-10029156-DE1_Shrinkwrap_1.001");
        endSupport.position=new Vector3(0,0,1.80f);
        model.transform.rotation=Quaternion.LookRotation(direction,Vector3.up);
        model.transform.localScale=Vector3.one*.55f;
        var belt=nodes.First(t=>t.name=="05-10274069-DE1_Simplify_1").GetComponent<Renderer>();
        Vector3 center=pickup-direction*.43f;
        model.transform.position+=new Vector3(center.x,surfaceHeight-.015f,center.z)-new Vector3(belt.bounds.center.x,belt.bounds.max.y,belt.bounds.center.z);
        foreach(var renderer in model.GetComponentsInChildren<MeshRenderer>())
        {
            renderer.sharedMaterials=renderer.sharedMaterials.Select(MaterialFor).ToArray();
            var collider=renderer.gameObject.AddComponent<MeshCollider>();collider.sharedMesh=renderer.GetComponent<MeshFilter>().sharedMesh;
        }
        // Adjustable lower legs extend the native support frames down to the floor.
        foreach(var support in nodes.Where(t=>t.name.StartsWith("05-10029156")))
        {
            var bounds=support.GetComponent<Renderer>().bounds;
            foreach(float side in new[]{-.10f,.10f})
            {
                var leg=GameObject.CreatePrimitive(PrimitiveType.Cube);leg.name="Adjustable support leg";
                leg.transform.SetParent(root.transform,true);
                leg.transform.rotation=model.transform.rotation;
                Vector3 p=bounds.center+Vector3.Cross(Vector3.up,direction)*side;
                leg.transform.position=new Vector3(p.x,bounds.min.y*.5f,p.z);
                leg.transform.localScale=new Vector3(.04f,bounds.min.y+.005f,.04f);
                leg.GetComponent<Renderer>().sharedMaterial=MaterialForColor(new Color(.38f,.43f,.48f),"Legs");
            }
        }
        var surface=GameObject.CreatePrimitive(PrimitiveType.Cube);surface.name="Stopped pickup belt surface";
        surface.transform.SetParent(root.transform,true);surface.transform.rotation=model.transform.rotation;
        surface.transform.position=new Vector3(center.x,surfaceHeight-.012f,center.z);
        surface.transform.localScale=new Vector3(.30f,.024f,1.10f);
        surface.GetComponent<Renderer>().sharedMaterial=MaterialForColor(new Color(.065f,.075f,.085f),"Rubber");
        control.beltSurface=surface.transform;
        control.treadMarkers=new Transform[10];
        for(int i=0;i<control.treadMarkers.Length;i++)
        {
            var marker=GameObject.CreatePrimitive(PrimitiveType.Cube);marker.name="Moving belt tread";
            Object.DestroyImmediate(marker.GetComponent<Collider>());
            marker.transform.SetParent(root.transform,true);marker.transform.rotation=model.transform.rotation;
            marker.transform.localScale=new Vector3(.27f,.002f,.008f);
            marker.transform.position=surface.transform.position+direction*(i*.11f-.55f)+Vector3.up*.014f;
            marker.GetComponent<Renderer>().sharedMaterial=MaterialForColor(new Color(.15f,.17f,.19f),"Tread");
            control.treadMarkers[i]=marker.transform;
        }
        control.robot=cycle.robot;control.raw=cycle.raw;
        control.pickupPosition=pickup;control.feedStart=pickup-direction*.85f;
        cycle.conveyor=control;
        var supply=GameObject.Find("Raw Material Supply Table");if(supply)Object.DestroyImmediate(supply);
        EditorUtility.SetDirty(cycle);
        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());AssetDatabase.SaveAssets();
        File.WriteAllText("ConveyorInstalled.txt","pickup="+pickup+" feedStart="+control.feedStart+" surface="+surfaceHeight+" direction="+direction+"\n"+string.Join("\n",root.GetComponentsInChildren<Renderer>().Select(r=>r.name+" center="+r.bounds.center+" size="+r.bounds.size)));
        if(File.Exists("ConveyorInstall.request"))File.Delete("ConveyorInstall.request");
        Debug.Log("[ConveyorSetup] Feed conveyor installed and scene saved.");
    }
    static Material MaterialFor(Material original) => MaterialForColor(original.color,original.name);
    static Material MaterialForColor(Color color,string name)
    {
        string path="Assets/Materials/Conveyor_"+name+".mat";
        var existing=AssetDatabase.LoadAssetAtPath<Material>(path);if(existing)return existing;
        var material=new Material(Shader.Find("Universal Render Pipeline/Lit"));material.color=color;
        AssetDatabase.CreateAsset(material,path);return material;
    }
    [MenuItem("Tools/EPIC/Inspect Conveyor")]
    public static void Inspect()
    {
        if(File.Exists("ConveyorModelReport.txt"))return;
        var asset=AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if(!asset)return;
        var instance=Object.Instantiate(asset);
        instance.hideFlags=HideFlags.HideAndDontSave;
        var report=new StringBuilder();
        foreach(var t in instance.GetComponentsInChildren<Transform>())
        {
            report.AppendLine(t.name+" parent="+(t.parent?t.parent.name:"ROOT")+" pos="+t.position.ToString("F4")+" scale="+t.lossyScale.ToString("F4"));
            var r=t.GetComponent<Renderer>();
            if(!r)continue;
            report.AppendLine("  center="+r.bounds.center.ToString("F4")+" size="+r.bounds.size.ToString("F4"));
            foreach(var m in r.sharedMaterials)if(m)report.AppendLine("  material="+m.name+" color="+m.color);
        }
        var cycle=Object.FindFirstObjectByType<RobotMachiningCycle>();
        if(cycle){report.AppendLine("SOURCE="+cycle.raw.position.ToString("F4"));foreach(var r in cycle.raw.GetComponentsInChildren<Renderer>())report.AppendLine("RAW="+r.bounds.center.ToString("F4")+" size="+r.bounds.size.ToString("F4"));}
        File.WriteAllText("ConveyorModelReport.txt",report.ToString());
        Object.DestroyImmediate(instance);
        Debug.Log("[ConveyorSetup] Model inspection complete.");
    }
}
