using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using System.Linq;
using UnityEditor.SceneManagement;

public static class MachineModelSetup
{
    const string ModelPath="Assets/Models/Haas Mill Latest.fbx";
 [MenuItem("Tools/EPIC/Use Latest Machine")]
 public static void Install() {
   if(EditorApplication.isPlayingOrWillChangePlaymode)return;
   var cycle=Object.FindFirstObjectByType<RobotMachiningCycle>();
   var asset=AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
   var old=GameObject.Find("Haas Mill - Open Loading Bay");
   if(!cycle || !asset || !old || old.GetComponent<MachineOperationController>())return;
   var machine=Object.Instantiate(asset);machine.name=old.name;
   Quaternion heading=Quaternion.Euler(0,-65,0);
   machine.transform.rotation=heading*asset.transform.rotation;machine.transform.localScale*=1.4f;
   var nodes=machine.GetComponentsInChildren<Transform>();
   var table=nodes.First(t=>t.name=="37-1466-DEF");
   var tableBounds=table.GetComponent<Renderer>().bounds;
   machine.transform.position+=new Vector3(.95f,1.28f,-.44f)-new Vector3(tableBounds.center.x,tableBounds.max.y,tableBounds.center.z);
   // The FBX also contains a second loose vice at the world origin. Keep the
   // installed vice on the table and omit those detached import leftovers.
   foreach(var t in machine.transform.Cast<Transform>().ToArray())if(t.name.StartsWith("SCHUNK"))Object.DestroyImmediate(t.gameObject);
   foreach(var renderer in machine.GetComponentsInChildren<MeshRenderer>()) {
     var originals=renderer.sharedMaterials;
     renderer.sharedMaterials=originals.Select(m=> {
       var mat=new Material(Shader.Find("Universal Render Pipeline/Lit"));mat.name=m.name;
       mat.color=m.color;mat.EnableKeyword("_EMISSION");
       string path="Assets/Materials/LatestMachine_"+m.name+".mat";
       var existing=AssetDatabase.LoadAssetAtPath<Material>(path);
       if(existing){Object.DestroyImmediate(mat);return existing;}
       AssetDatabase.CreateAsset(mat,path);return mat;
     }).ToArray();
     var collider=renderer.gameObject.AddComponent<MeshCollider>();collider.sharedMesh=renderer.GetComponent<MeshFilter>().sharedMesh;
   }
   foreach(var t in nodes.Where(t=>t && (t.name=="CONV-SHELF-VF-SM" || t.name=="CONV-HOLDER-VF-XS")))t.position+=heading*Vector3.right*.85f;
   var control=machine.AddComponent<MachineOperationController>();control.robot=cycle.robot;
   control.door=nodes.First(t=>t.name=="37-4532-DEF-DOOR");
   control.doorClosed=control.door.position-heading*Vector3.right*(.442f*1.4f);
   control.doorOpen=control.doorClosed+heading*Vector3.right*1.23f;
   control.door.position=control.doorClosed;
   control.carriage=nodes.First(t=>t.name=="37-1481-DEF");
   control.carriageLoading=control.carriage.position;
   control.carriageProcessing=control.carriageLoading-heading*Vector3.forward*.08f;
   control.indicator=nodes.First(t=>t.name=="INDICATOR LIGHT").GetComponent<Renderer>();
   // Leave enough room above the blank for the tool, including during table travel.
   var column=nodes.First(t=>t.name=="37-1465-DEF");column.position+=Vector3.up*.20f;
   var spindle=nodes.First(t=>t.name.StartsWith("20T Spindle"));
   var pivot=new GameObject("Spindle Rotation Pivot").transform;
   pivot.position=spindle.GetComponent<Renderer>().bounds.center;pivot.SetParent(column,true);spindle.SetParent(pivot,true);
   cycle.spindle=pivot;
   cycle.machine=control;
   cycle.machinePosition.y=1.55f;cycle.machineTableHeight=1.28f;
   var vice=nodes.First(t=>t.name=="SCHUNK-KSC-F / S2 80.001");
   vice.position+=heading*(Vector3.right*.258f-Vector3.forward*.021f);
   var support=GameObject.CreatePrimitive(PrimitiveType.Cylinder);support.name="Workpiece fixture support";
   support.transform.position=new Vector3(.95f,1.42f,-.44f);support.transform.localScale=new Vector3(.105f,.04f,.105f);
   support.transform.SetParent(control.carriage,true);
   support.GetComponent<Renderer>().sharedMaterial=table.GetComponent<Renderer>().sharedMaterial;
   new GameObject("Fixture Alignment v2").transform.SetParent(control.transform,false);
   cycle.robot.machineApproach.position+=Vector3.up*.10f;
   cycle.robot.machineHold.position+=Vector3.up*.10f;
   Object.DestroyImmediate(old);
   EditorUtility.SetDirty(cycle);
   EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
   EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());AssetDatabase.SaveAssets();
   File.WriteAllText("LatestMachineInstalled.txt",string.Join("\n",machine.GetComponentsInChildren<Renderer>().Select(r=>r.name+" center="+r.bounds.center+" size="+r.bounds.size)));
   Debug.Log("[MachineSetup] Latest machine installed with sliding door, carriage and status light.");
 }
    [MenuItem("Tools/EPIC/Inspect Latest Machine")]
    public static void Inspect()
    {
        var asset=AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if(!asset)return;
        var instance=Object.Instantiate(asset);
        instance.hideFlags=HideFlags.HideAndDontSave;
        var report=new StringBuilder();
        foreach(var t in instance.GetComponentsInChildren<Transform>())
        {
            report.AppendLine(t.name+" parent="+(t.parent?t.parent.name:"ROOT")+" pos="+t.position.ToString("F4")+" local="+t.localPosition.ToString("F4")+" rot="+t.localEulerAngles.ToString("F2"));
            var renderer=t.GetComponent<Renderer>();
            if(!renderer)continue;
            report.AppendLine("  bounds center="+renderer.bounds.center.ToString("F4")+" size="+renderer.bounds.size.ToString("F4"));
            foreach(var material in renderer.sharedMaterials)
                if(material)report.AppendLine("  material="+material.name+" color="+material.color);
        }
        File.WriteAllText("LatestMachineModel.txt",report.ToString());
        Object.DestroyImmediate(instance);
        Debug.Log("[MachineSetup] Latest model hierarchy inspected.");
    }
}
