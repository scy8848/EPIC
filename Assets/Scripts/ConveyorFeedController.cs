using System.Collections;
using UnityEngine;

public class ConveyorFeedController : MonoBehaviour
{
    public RobotGrabController robot;
    public Rigidbody raw;
    public Transform beltSurface;
    public Vector3 feedStart, pickupPosition;
    public float feedSeconds=1.5f;
    public bool IsMoving { get; private set; }
    public bool IsReady { get; private set; }
    public Transform[] treadMarkers;
    Vector3 direction;
    Quaternion rawRotation;
    float beltPhase;

    void Awake()
    {
        rawRotation=raw.rotation;
        direction=(pickupPosition-feedStart).normalized;
        StageNext();
    }
    public void StageNext()
    {
        IsMoving=false;IsReady=false;
        raw.transform.SetParent(null,true);
        raw.interpolation=RigidbodyInterpolation.None;raw.isKinematic=true;
        raw.transform.SetPositionAndRotation(feedStart,rawRotation);
        raw.gameObject.SetActive(true);
    }
    public IEnumerator Deliver()
    {
        if(IsReady)yield break;
        raw.isKinematic=true;raw.interpolation=RigidbodyInterpolation.None;
        Vector3 start=raw.position;
        IsMoving=true;
        for(float elapsed=0;elapsed<feedSeconds;)
        {
            if(!robot.IsDamaged)
            {
                elapsed+=Time.deltaTime;
                raw.transform.position=Vector3.Lerp(start,pickupPosition,Mathf.SmoothStep(0,1,elapsed/feedSeconds));
            }
            yield return null;
        }
        raw.transform.position=pickupPosition;
        IsMoving=false;
        // A stopped conveyor presents one blank. The robot never chases a moving part.
        for(float elapsed=0;elapsed<.15f;)
        {
            if(!robot.IsDamaged)elapsed+=Time.deltaTime;
            yield return null;
        }
        IsReady=true;
        Debug.Log("[Conveyor] Blank delivered; belt stopped before robot pickup.");
    }
    void Update()
    {
        if(!IsMoving || robot.IsDamaged)return;
        beltPhase=Mathf.Repeat(beltPhase+Time.deltaTime*.55f,1.10f);
        for(int i=0;i<treadMarkers.Length;i++)
        {
            float offset=Mathf.Repeat(i*1.10f/treadMarkers.Length+beltPhase,1.10f);
            treadMarkers[i].position=beltSurface.position+direction*(offset-.55f)+Vector3.up*.014f;
        }
    }
}
