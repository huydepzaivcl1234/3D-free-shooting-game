using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
namespace Survivor.CrowdPrototype
{
 // Isolated rendering/simulation trial. Does not impersonate CharacterHealth or migrate cards.
 public sealed class Crowd10K : MonoBehaviour
 {
  public Crowd10KData data;
  public Transform target;
  public Camera view;
  public int Alive {get;private set;}
  public int Drawn {get;private set;}
  public int Triangles {get;private set;}
  public double SimulationMs {get;private set;}
  public bool benchmark;
  public bool compareIndirectRendering;
  public float benchmarkWarmupSeconds=4,benchmarkSampleSeconds=12;
  private CrowdIndirectRenderer indirect;
  private CrowdSpeciesCatalog.Species[] species;
  private CrowdSpeciesBatch mixedBatch;
  private bool useIndirect;
  public string outputDirectory="";
  public int[] benchmarkCounts={1000,5000,10000,20000};
  NativeArray<float3> positions,next;
  NativeArray<float> health, climb, nextClimb, ground;
  NativeParallelMultiHashMap<int2,int> cells;
  int groundWidth, groundCursor, samplesCached;
  float groundStep;
  readonly Matrix4x4[][] matrices=new Matrix4x4[4][];
  readonly Vector4[][] animation=new Vector4[4][];
  readonly int[] counts=new int[4];
  readonly int[] triangleCounts=new int[4];
  readonly Matrix4x4[] batch=new Matrix4x4[1023];
  readonly Vector4[] animBatch=new Vector4[1023];
  readonly Plane[] planes=new Plane[6];
  MaterialPropertyBlock properties;
  readonly Stopwatch clock=new Stopwatch();
  readonly List<float> timings=new List<float>(10000);
  readonly List<float> simTimings=new List<float>(10000);
  float yaw,pitch=48,distance=135,elapsed,hudTimer,fireTimer;
  string hud="";
  int stage;
  long sampleDrawn,sampleTriangles;
  void Start(){
   if(!view)view=Camera.main;
   properties=new MaterialPropertyBlock();
   var args=Environment.GetCommandLineArgs();
   int arg=Array.IndexOf(args,"--crowd-benchmark");if(arg>=0){benchmark=true;if(arg+1<args.Length)outputDirectory=args[arg+1];}
   QualitySettings.vSyncCount=0;Application.targetFrameRate=-1;
   useIndirect = data.useIndirectRendering && !compareIndirectRendering;
   ResetCrowd(benchmark?benchmarkCounts[0]:data.count);
  }
  public void ResetCrowd(int count){
   Dispose();count=Mathf.Clamp(count,1,100000);positions=new NativeArray<float3>(count,Allocator.Persistent);next=new NativeArray<float3>(count,Allocator.Persistent);health=new NativeArray<float>(count,Allocator.Persistent);cells=new NativeParallelMultiHashMap<int2,int>(count,Allocator.Persistent);
   climb = new NativeArray<float>(count, Allocator.Persistent);
   nextClimb = new NativeArray<float>(count, Allocator.Persistent);
   groundStep = Mathf.Max(.25f, data.groundCellSize);
   groundWidth = Mathf.CeilToInt(data.mapHalfSize * 2 / groundStep) + 1;
   ground = new NativeArray<float>(groundWidth * groundWidth, Allocator.Persistent);
   for (int i = 0; i < ground.Length; i++) ground[i] = float.NaN;
   groundCursor = samplesCached = 0;
   int side=Mathf.CeilToInt(Mathf.Sqrt(count));float spacing=math.min(1.2f,data.mapHalfSize*1.8f/side);
   for(int i=0;i<count;i++){positions[i]=new float3((i%side-(side-1)*.5f)*spacing,0,(i/side-(side-1)*.5f)*spacing);health[i]=30;}
   for(int l=0;l<4;l++){triangleCounts[l]=(int)data.meshes[l].GetIndexCount(0)/3;matrices[l]=new Matrix4x4[count];animation[l]=new Vector4[count];}
   if (useIndirect)
   {
    var catalog = data.useSpeciesCatalog ? Resources.Load<CrowdSpeciesCatalog>("CrowdSpeciesCatalog") : null;
    if (catalog && catalog.species.Length > 0 && SystemInfo.supportsComputeShaders &&
        SystemInfo.supportsInstancing && SystemInfo.supportsIndirectArgumentsBuffer)
    {
     species = catalog.species;
     mixedBatch = new CrowdSpeciesBatch(Mathf.Max(1, Mathf.CeilToInt(count / (float)species.Length)));
    }
    else indirect = CrowdIndirectRenderer.Create(data,count);
   }
   Alive=count;elapsed=0;timings.Clear();simTimings.Clear();sampleDrawn=sampleTriangles=0;
  }
  void Update(){
   if(!positions.IsCreated)return;
   float dt=math.min(Time.deltaTime,.04f);
   if(!benchmark){
    var k=Keyboard.current;float2 move=float2.zero;if(k!=null){move.x=(k.dKey.isPressed?1:0)-(k.aKey.isPressed?1:0);move.y=(k.wKey.isPressed?1:0)-(k.sKey.isPressed?1:0);}
    var motion=Quaternion.Euler(0,yaw,0)*new Vector3(move.x,0,move.y);target.position+=motion*dt*12;
    target.position=new Vector3(Mathf.Clamp(target.position.x,-60,60),1,Mathf.Clamp(target.position.z,-60,60));
    var mouse=Mouse.current;if(mouse!=null){if(mouse.rightButton.isPressed){var d=mouse.delta.ReadValue();yaw+=d.x*.15f;pitch=Mathf.Clamp(pitch-d.y*.15f,20,85);}distance=Mathf.Clamp(distance-mouse.scroll.ReadValue().y*.04f,12,150);}
    fireTimer-=dt;if(k!=null&&k.spaceKey.isPressed&&fireTimer<=0){fireTimer=.1f;int nearest=-1;float best=400;for(int i=0;i<positions.Length;i++){if(health[i]<=0)continue;float dd=math.distancesq(positions[i],(float3)target.position);if(dd<best){best=dd;nearest=i;}}if(nearest>=0){health[nearest]-=15;if(health[nearest]<=0)Alive--;}}
   }
   clock.Restart();
   SampleOccupiedGround();
   cells.Clear();
   float neighbourCell = Mathf.Max(1.2f, data.separationRadius * 2f + .12f);
   var hash = new HashJob
   {
    positions = positions, health = health, cells = cells.AsParallelWriter(), cellSize = neighbourCell
   }.Schedule(positions.Length, 128);
   new MoveJob
   {
    positions = positions, next = next, health = health, cells = cells, target = target.position,
    speed = data.movementSpeed, radius = data.separationRadius, dt = dt, halfSize = data.mapHalfSize,
    cellSize = neighbourCell, ground = ground, groundWidth = groundWidth, groundStep = groundStep,
    climb = climb, nextClimb = nextClimb, maxClimb = data.maximumClimbHeight
   }.Schedule(positions.Length, 64, hash).Complete();
   (positions, next) = (next, positions);
   (climb, nextClimb) = (nextClimb, climb);
   clock.Stop();
   SimulationMs = clock.Elapsed.TotalMilliseconds;
   view.transform.position=target.position+Quaternion.Euler(pitch,yaw,0)*new Vector3(0,0,-distance);view.transform.LookAt(target.position);
   GeometryUtility.CalculateFrustumPlanes(view,planes);Array.Clear(counts,0,4);Drawn=Triangles=0;
   var t=target.position;
   if (mixedBatch != null) DrawMixed(t);
   else if (indirect != null) DrawIndirect(t);
   else {
   for(int i=0;i<positions.Length;i++){
    if(health[i]<=0)continue;Vector3 p=positions[i];if(!GeometryUtility.TestPlanesAABB(planes,new Bounds(p+Vector3.up,Vector3.one*3)))continue;
    float d=Vector3.Distance(p,t);float cameraDistance=Vector3.Distance(p,view.transform.position);int lod=cameraDistance<data.lodDistances[0]?0:cameraDistance<data.lodDistances[1]?1:cameraDistance<data.lodDistances[2]?2:3;
    int slot=counts[lod]++;Vector3 direction=t-p;direction.y=0;var rotation=direction.sqrMagnitude>.001f?Quaternion.LookRotation(direction):Quaternion.identity;
    matrices[lod][slot]=Matrix4x4.TRS(p,rotation,Vector3.one);
    int clip=d<1.8f?2:1;float phase=(i%8)/8f;
    animation[lod][slot]=new Vector4(clip*data.framesPerClip,data.framesPerClip,phase,1/data.clipLengths[clip]);Drawn++;Triangles+=triangleCounts[lod];
   }
   for(int l=0;l<4;l++)for(int offset=0;offset<counts[l];offset+=1023){int n=Math.Min(1023,counts[l]-offset);Array.Copy(matrices[l],offset,batch,0,n);Array.Copy(animation[l],offset,animBatch,0,n);properties.SetVectorArray("_AnimData",animBatch);var rp=new RenderParams(data.material){camera=view,matProps=properties,shadowCastingMode=ShadowCastingMode.Off,receiveShadows=false,worldBounds=new Bounds(Vector3.zero,Vector3.one*400)};Graphics.RenderMeshInstanced(rp,data.meshes[l],0,batch,n);}
   }
   if(benchmark)Measure();
   hudTimer-=Time.unscaledDeltaTime;if(hudTimer<=0){hudTimer=.25f;string draws=mixedBatch!=null?$"{mixedBatch.ActiveSpecies} species | GPU indirect":indirect!=null?"GPU culling / indirect draws":$"{Drawn:N0} drawn | {Triangles:N0} triangles";hud=$"GPU CROWD PROTOTYPE | {Alive:N0} NPCs | {draws}\nAll {positions.Length:N0} simulated every frame | Ground cells {samplesCached:N0} | Simulation {SimulationMs:F2} ms | {1/Time.unscaledDeltaTime:F0} FPS\nWASD target  |  RMB orbit  |  Wheel zoom  |  Space damage/death trial";}
  }
  private void SampleOccupiedGround()
  {
   int remaining = Mathf.Max(0, data.groundSamplesPerFrame);
   int visits = Mathf.Min(positions.Length, remaining * 8);
   for (int i = 0; i < visits && remaining > 0; i++)
   {
    int index = groundCursor;
    groundCursor = (groundCursor + 1) % positions.Length;
    if (health[index] <= 0) continue;
    int2 cell = GroundCell(positions[index].xz, data.mapHalfSize, groundStep, groundWidth);
    int key = cell.y * groundWidth + cell.x;
    if (!float.IsNaN(ground[key])) continue;
    float2 point = ((float2)cell + .5f) * groundStep - data.mapHalfSize;
    ground[key] = Physics.Raycast(new Vector3(point.x, 64f, point.y), Vector3.down, out var hit,
     128f, data.groundLayers, QueryTriggerInteraction.Ignore) ? hit.point.y : 0f;
    remaining--;
    samplesCached++;
   }
  }

  private static int2 GroundCell(float2 point, float halfSize, float step, int width) =>
   math.clamp((int2)math.floor((point + halfSize) / step), 0, width - 1);

  void Measure(){
   elapsed+=Time.unscaledDeltaTime;if(elapsed<benchmarkWarmupSeconds)return;
   timings.Add(Time.unscaledDeltaTime*1000);simTimings.Add((float)SimulationMs);sampleDrawn+=Drawn;sampleTriangles+=Triangles;
   if(elapsed<benchmarkWarmupSeconds+benchmarkSampleSeconds)return;timings.Sort();simTimings.Sort();
   var result=new Result{npcCount=positions.Length,samples=timings.Count,medianMs=timings[timings.Count/2],p95Ms=timings[(int)(timings.Count*.95f)],meanMs=Mean(timings),simulationMedianMs=simTimings[simTimings.Count/2],drawnMean=indirect!=null||mixedBatch!=null?-1:(float)sampleDrawn/timings.Count,trianglesMean=indirect!=null||mixedBatch!=null?-1:(float)sampleTriangles/timings.Count,width=Screen.width,height=Screen.height,device=SystemInfo.graphicsDeviceName,cpu=SystemInfo.processorType,unity=Application.unityVersion,editor=Application.isEditor,allSimulated=true,renderPath=mixedBatch!=null?"MultiSpeciesGPUIndirect":indirect!=null?"GPUIndirect":useIndirect?"Fallback":"CPUInstanced",speciesCount=mixedBatch?.ActiveSpecies??1};
   result.fps=1000/result.medianMs;
   if(!string.IsNullOrEmpty(outputDirectory)){Directory.CreateDirectory(outputDirectory);File.WriteAllText(Path.Combine(outputDirectory,"Crowd"+positions.Length+"-"+result.renderPath+"-"+(Application.isEditor?"Editor":"Player")+".json"),JsonUtility.ToJson(result,true));ScreenCapture.CaptureScreenshot(Path.Combine(outputDirectory,"Crowd"+positions.Length+"-"+result.renderPath+"-"+(Application.isEditor?"Editor":"Player")+".png"));}
   UnityEngine.Debug.Log(JsonUtility.ToJson(result));
   if(compareIndirectRendering&&!useIndirect)useIndirect=true;
   else {useIndirect=!compareIndirectRendering && data.useIndirectRendering;stage++;}
   // Defer population changes until after screenshot's end-of-frame readback.
   if(stage<benchmarkCounts.Length)StartCoroutine(NextStage());else benchmark=false;
  }
  System.Collections.IEnumerator NextStage(){benchmark=false;yield return new WaitForEndOfFrame();yield return null;ResetCrowd(benchmarkCounts[stage]);benchmark=true;}
  static float Mean(List<float> v){double sum=0;foreach(var x in v)sum+=x;return(float)(sum/v.Count);}
  [Serializable] public class Result{public int npcCount,samples,width,height,speciesCount;public float medianMs,p95Ms,meanMs,fps,simulationMedianMs,drawnMean,trianglesMean;public string device,cpu,unity,renderPath;public bool editor,allSimulated;}
  void OnGUI(){GUI.Box(new Rect(12,12,880,82),hud);}
  private void DrawMixed(Vector3 goal)
  {
   mixedBatch.Begin();
   for (int i = 0; i < positions.Length; i++)
   {
    if (health[i] <= 0) continue;
    Vector3 point = positions[i];
    Vector3 direction = goal - point;
    direction.y = 0f;
    int pose = direction.sqrMagnitude > 1.8f * 1.8f ? 1 : 0;
    Quaternion rotation = direction.sqrMagnitude > .001f ? Quaternion.LookRotation(direction) : Quaternion.identity;
    mixedBatch.Add(species[i % species.Length].visuals, Matrix4x4.TRS(point, rotation, Vector3.one),
     pose, i / species.Length, Color.clear);
   }
   mixedBatch.Draw(view, new Bounds(Vector3.zero, Vector3.one * 400), 0, false);
  }

  private void DrawIndirect(Vector3 goal)
  {
   int count=0;
   for(int i=0;i<positions.Length;i++)
   {
    if(health[i]<=0)continue;
    Vector3 point=positions[i],direction=goal-point;
    int clip=direction.sqrMagnitude<1.8f*1.8f?2:1;
    direction.y=0;
    Quaternion rotation=direction.sqrMagnitude>.001f?Quaternion.LookRotation(direction):Quaternion.identity;
    indirect.Input[count++]=new CrowdIndirectRenderer.Instance
    {
     transform=Matrix4x4.TRS(point,rotation,Vector3.one),
     animation=new Vector4(clip*data.framesPerClip,data.framesPerClip,(i%8)/8f,1/data.clipLengths[clip]),
     tint=Vector4.one,
     sphere=new Vector4(point.x,point.y,point.z,data.meshes[0].bounds.extents.magnitude)
    };
   }
   indirect.Draw(count,view,new Bounds(Vector3.zero,Vector3.one*400),0,false);
  }
  void OnDestroy(){Dispose();}
  void Dispose(){mixedBatch?.Dispose();mixedBatch=null;species=null;indirect?.Dispose();indirect=null;if(positions.IsCreated)positions.Dispose();if(next.IsCreated)next.Dispose();if(health.IsCreated)health.Dispose();if(cells.IsCreated)cells.Dispose();if(climb.IsCreated)climb.Dispose();if(nextClimb.IsCreated)nextClimb.Dispose();if(ground.IsCreated)ground.Dispose();}
  [BurstCompile]
  private struct HashJob : IJobParallelFor
  {
   [ReadOnly] public NativeArray<float3> positions;
   [ReadOnly] public NativeArray<float> health;
   public NativeParallelMultiHashMap<int2,int>.ParallelWriter cells;
   public float cellSize;

   public void Execute(int i)
   {
    if (health[i] > 0) cells.Add((int2)math.floor(positions[i].xz / cellSize), i);
   }
  }

  [BurstCompile]
  private struct MoveJob : IJobParallelFor
  {
   [ReadOnly] public NativeArray<float3> positions;
   [ReadOnly] public NativeArray<float> health, ground, climb;
   [ReadOnly] public NativeParallelMultiHashMap<int2,int> cells;
   [WriteOnly] public NativeArray<float3> next;
   [WriteOnly] public NativeArray<float> nextClimb;
   public float3 target;
   public float speed, radius, dt, halfSize, cellSize, groundStep, maxClimb;
   public int groundWidth;

   public void Execute(int i)
   {
    float3 point = positions[i];
    float previousLift = climb[i];
    nextClimb[i] = previousLift;
    next[i] = point;
    if (health[i] <= 0) return;

    float2 toTarget = target.xz - point.xz;
    bool chasing = math.lengthsq(toTarget) > 1.8f * 1.8f;
    float2 direction = chasing ? math.normalizesafe(toTarget) : float2.zero;
    float2 velocity = direction * speed;
    float2 push = float2.zero;
    float support = 0f;
    float groundHeight = point.y - previousLift;
    float reach = radius * 2f;
    float neighbourRadius = reach + .12f;
    int2 cell = (int2)math.floor(point.xz / cellSize);
    int checkedCount = 0;
    for (int y = -1; y <= 1 && checkedCount < 48; y++)
    for (int x = -1; x <= 1 && checkedCount < 48; x++)
    {
     if (!cells.TryGetFirstValue(cell + new int2(x, y), out int neighbour, out var iterator)) continue;
     do
     {
      if (neighbour == i) continue;
      checkedCount++;
      float2 separation = point.xz - positions[neighbour].xz;
      float square = math.lengthsq(separation);
      if (square <= .000001f || square >= neighbourRadius * neighbourRadius) continue;
      float distance = math.sqrt(square);
      float2 outward = separation / distance;
      float overlap = math.max(0f, reach - distance);
      bool pushingForward = chasing && math.dot(direction, outward) < -.55f;
      if (maxClimb > 0f && pushingForward && overlap > 0f &&
          math.abs(groundHeight - (positions[neighbour].y - climb[neighbour])) < .55f)
      {
       float weight = math.saturate(overlap / math.max(.01f, reach * .4f));
       support = math.max(support, math.min(maxClimb, climb[neighbour] + radius * 1.65f) * weight);
       overlap *= 1f - weight * .85f;
      }
      if (math.abs(positions[neighbour].y - point.y) < reach)
      {
       push += outward * overlap * 5f;
       if (!pushingForward || maxClimb <= 0f)
        velocity -= outward * math.min(0f, math.dot(velocity, outward));
      }
     } while (checkedCount < 48 && cells.TryGetNextValue(out neighbour, ref iterator));
    }

    velocity += push;
    velocity *= math.min(1f, speed * 1.5f / math.max(.001f, math.length(velocity)));
    point.xz = math.clamp(point.xz + velocity * dt, -halfSize, halfSize);
    int2 groundCell = GroundCell(point.xz, halfSize, groundStep, groundWidth);
    float sampledHeight = ground[groundCell.y * groundWidth + groundCell.x];
    float lift = math.lerp(previousLift, support, math.saturate(dt * (support > previousLift ? 5f : 3f)));
    point.y = (math.isnan(sampledHeight) ? groundHeight : sampledHeight) + lift;
    nextClimb[i] = lift;
    next[i] = point;
   }
  }
 }
}
