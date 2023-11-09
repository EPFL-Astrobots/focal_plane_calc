﻿// See https://aka.ms/new-console-template for more information

using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidworksAutomationTool;
using System.Diagnostics;
using static SolidworksAutomationTool.ScaffoldFunctions;

/* Define some parameters here. These parameters should be configurable in the GUI */
// TODO: param: chamfer length
const double chamferLength = 10.5e-3;               // in meters
// TODO: param: pin hole diameter
const double pinHoleDiameter = 2.5e-3;              // in meters
// TODO: param: pin hole depth
const double pinHoleDepth = 6e-3;                   // in meters

// TODO: param: bestFitSphereRadius.
const double bestFitSphereRadius = 11045.6e-3;      // in meters
// TODO: param: focal plane thickness
double outerRimHeight = 200e-3;                     // in meters
// TODO: param: distance between the support surface to top surface definition
double supportToTopSurfaceDistance = 30e-3;         // in meters
// TODO: create horizontal line (for flat bottom surface). Need a better name here
double bottomSurfaceRadius = 663.27e-3;
// TODO: define the side length of the equilateral triangle
double equilateralTriangleSideLength = 74.5e-3;

Console.WriteLine("Welcome to the LASTRO Solidworks Automation Tool!");

// import the point cloud (the grid text file generated by the python script)
/* Uncomment the Console.ReadLine() to restore normal path input. Currently they are commented out for debug purpose */
Console.WriteLine("Please enter the path of the FRONT grid point cloud txt file");
string frontGridPointCloudFilePath = Console.ReadLine();
//string frontGridPointCloudFilePath = Path.GetFullPath(@"..\..\..\..\..\Results_examples\2023-10-20-11-39-57_front_grid_indiv_63.txt"); // For debug use.

Console.WriteLine("Reading front grid point cloud file ...");
PointCloud frontGridPointCloud = new();
frontGridPointCloud.ReadPointCloudFromTxt(frontGridPointCloudFilePath, Units.Millimeter);
Console.WriteLine("Completed reading point cloud file");

Console.WriteLine("Please enter the path of the BACK grid point cloud txt file");
string backGridPointCloudFilePath = Console.ReadLine();
//string backGridPointCloudFilePath = Path.GetFullPath(@"..\..\..\..\..\Results_examples\2023-10-20-11-39-57_back_grid_indiv_63.txt"); // For debug use

Console.WriteLine("Reading back grid point cloud file ...");
PointCloud backGridPointCloud = new();
backGridPointCloud.ReadPointCloudFromTxt(backGridPointCloudFilePath, Units.Millimeter);
Console.WriteLine("Completed reading point cloud file");

if ( frontGridPointCloud.point3Ds.Count != backGridPointCloud.point3Ds.Count )
{
    Console.WriteLine("WARNING: the number of points on the front and back grid are not the same. Is this intentional?");
}

// remove the offset in Z direction with the best-fit sphere's radius. Otherwise the points are placed at a super far place
Console.WriteLine("Removing offsets in Z axis for all points ...");

// Using the add operation because the z coordinates in the point clouds are negative. We want to offset them to close to zero
foreach ((Point3D frontPoint, Point3D backPoint) in frontGridPointCloud.point3Ds.Zip(backGridPointCloud.point3Ds))
{
    frontPoint.z += bestFitSphereRadius;
    backPoint.z += bestFitSphereRadius;
}

// DEBUG use: check if the points are read in correctly
Console.WriteLine("\nFront grid point cloud with z-axis offset removed: ");
frontGridPointCloud.PrintPoint3Ds();
frontGridPointCloud.PrintModuleOrientations();

// DEBUG use: check if the points are read in correctly
Console.WriteLine("\nBack grid point cloud with z-axis offset removed: ");
backGridPointCloud.PrintPoint3Ds();
backGridPointCloud.PrintModuleOrientations();

Console.WriteLine("Starting SolidWorks Application ...");

// Solidworks related variable definitions
SldWorks? solidworksApp;
ModelDoc2 modulePart;
ModelView modelView;
// get solidworks and start it
const string solidWorkAppID = "SldWorks.Application";
solidworksApp = Activator.CreateInstance(Type.GetTypeFromProgID(solidWorkAppID)) as SldWorks;

if (solidworksApp == null)
{
    Console.WriteLine("SolidWorks could not be started. Exiting program now");
    return;
}

solidworksApp.Visible = true;
Console.WriteLine("Please wait a bit. SolidWorks should appear. If not, there is an error starting solidworks");

/* Start modeling the robot holder in the focal plane */
PromptAndWait("Press any key to create the robot-holder from the point clouds");
Console.WriteLine("Creating extrusion axes from point clouds ...");

// create a part
modulePart = solidworksApp.INewDocument2( solidworksApp.GetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swDefaultTemplatePart), 0, 0, 0);

// Get a handle to the FRONT, TOP, RIGHT planes
BasicReferenceGeometry basicRefGeometry = GetBasicReferenceGeometry(ref modulePart);

//PromptAndWait("Press any key to insert 3D sketch");
modulePart.SketchManager.Insert3DSketch(true);

// set the view to isometric. The empty string tells solidworks to use the view indicated by the swStandardViews_e enum.
modulePart.ShowNamedView2("", (int)swStandardViews_e.swIsometricView);

// disable user input box when adding dimensions
//solidworksApp.SetUserPreferenceToggle( (int)swUserPreferenceToggle_e.swInputDimValOnCreate, false );
DisableInputDimensionByUser(ref solidworksApp);

// disable view refreshing until points are created
modelView =(ModelView)modulePart.GetFirstModelView();
modelView.EnableGraphicsUpdate = false;
modulePart.SketchManager.AddToDB = true;

// try diabling feature tree updates to gain performance
modulePart.FeatureManager.EnableFeatureTree = false;
// EnableGraphicsUpdate affects whether to refresh the model view during a selection, such as IEntity::Select4 or IFeature::Select2.
modelView.EnableGraphicsUpdate = false;

// try to allocate space for front sketchpoints and back sketchpoints
List<SketchPoint> frontSketchPointList = new(frontGridPointCloud.point3Ds.Count);
List<SketchPoint> backSketchPointList = new(backGridPointCloud.point3Ds.Count);
List<SketchSegment> extrusionAxisList = new(frontSketchPointList.Count);

// Try iterating through two point clouds at the same time
foreach ( (Point3D frontPoint, Point3D backPoint) in frontGridPointCloud.point3Ds.Zip(backGridPointCloud.point3Ds))
{
    // create top and bottom points
    frontSketchPointList.Add( modulePart.SketchManager.CreatePoint(frontPoint.x, frontPoint.y , frontPoint.z ) );
    backSketchPointList.Add( modulePart.SketchManager.CreatePoint(backPoint.x , backPoint.y  , backPoint.z ) );

    // create axis of extrusion as construction lines
    extrusionAxisList.Add( modulePart.SketchManager.CreateLine(frontPoint.x , frontPoint.y , frontPoint.z, backPoint.x, backPoint.y, backPoint.z) );
    // using fancy but convenient index-from-end operator (^), which is available in C# 8.0 and later, to get the last element in a list.
    extrusionAxisList[^1 ].ConstructionGeometry = true;
}

Console.WriteLine("Extrusion axes placement completed");

// EnableGraphicsUpdate affects whether to refresh the model view during a selection, such as IEntity::Select4 or IFeature::Select2.
modelView.EnableGraphicsUpdate = true;

// Sometimes the camera is not pointing toward the part. So repoint the camera to the part.
modulePart.ViewZoomtofit2();

// The documentation says: Inserts a new 3D sketch in a model or closes the active sketch. ?? 
modulePart.SketchManager.Insert3DSketch(true);

// magic clear selection method
ClearSelection(ref modulePart);

// create another 3D sketch so that the extrusion axes are untouched
modulePart.SketchManager.Insert3DSketch(true);
ClearSelection(ref modulePart);

PromptAndWait("Press any key to create small segments");
Console.WriteLine("Creating small segments ...");

// According to solidworks api, we need to define a SelectData object and pass it into each selection call.
SelectionMgr swSelectionManager = (SelectionMgr)modulePart.SelectionManager;
SelectData swSelectData = swSelectionManager.CreateSelectData();

// Keep a list of the points that defines the positions of the support surfaces
List<SketchPoint> supportSurfaceMarkerPointList = new(frontSketchPointList.Count);

// disable graphics update to boost performance
modelView.EnableGraphicsUpdate = false;
// Create the small segments from the top surface
foreach ((SketchPoint frontSketchPoint, SketchPoint backSketchPoint, SketchSegment extrusionAxis) in frontSketchPointList.Zip(backSketchPointList, extrusionAxisList))
{
    // first create a small sketch point at the middle of an extrusion axis
    SketchPoint smallSegmentSketchPoint = modulePart.SketchManager.CreatePoint(   (frontSketchPoint.X + backSketchPoint.X) / 2,
                                            (frontSketchPoint.Y + backSketchPoint.Y) / 2,
                                            (frontSketchPoint.Z + backSketchPoint.Z) / 2);

    // constraint the point to be on coincide with the extrusion axis. Assuming the smallSegmentSketchPoint is already selected after creation
    extrusionAxis.Select4(true, swSelectData);
    MakeSelectedCoincide(ref modulePart);
    // clear previous selections, so that no unintentional selection
    ClearSelection(ref modulePart);

    // add a length dimension to the small segment
    frontSketchPoint.Select4(true, swSelectData);
    smallSegmentSketchPoint.Select4(true, swSelectData);
    AddDimensionToSelected(ref modulePart, supportToTopSurfaceDistance, frontSketchPoint);
    ClearSelection(ref modulePart);
    // save the support surface marker point to the list. It will be used in the later support surface extrusion
    supportSurfaceMarkerPointList.Add(smallSegmentSketchPoint);
}

Console.WriteLine("Small segment creation completed");
// enbale user input box for dimensions
EnableInputDimensionByUser(ref solidworksApp);

// restore settings to make solidworks operate as normal
modulePart.SketchManager.AddToDB = false;
modelView.EnableGraphicsUpdate = true;
modulePart.FeatureManager.EnableFeatureTree = true;

modulePart.SketchManager.Insert3DSketch(true);
ClearSelection(ref modulePart);
ZoomToFit(ref modulePart);

modulePart.SketchManager.AddToDB = true;

PromptAndWait("Press any key to revolve a pizza slice (1/6 of a pizza)");
Console.WriteLine("Revolving a pizza slice ...");

// define variables needed for pizza creation
double arcAngle = DegreeToRadian(15);

// TODO: verify Solidworks wants points to be defined in the local cartesian coordinate frame. - inside a sketch, yes
Point3D arcCenterPoint = new(0, -bestFitSphereRadius, 0);
Point3D arcStartPoint = new(0, 0, 0);
Point3D arcEndPoint = new(bestFitSphereRadius * Math.Sin(arcAngle), -bestFitSphereRadius * (1 - Math.Cos(arcAngle)), 0);

// select top reference plane
basicRefGeometry.topPlane.Select2(false, -1);
// create arc to form the curved top surface
modulePart.SketchManager.InsertSketch(true);

// TODO: find a good way to describe which line is which
DisableInputDimensionByUser(ref solidworksApp);

SketchArc arc = (SketchArc)modulePart.SketchManager.CreateArc(arcCenterPoint.x, arcCenterPoint.y, arcCenterPoint.z,
                                    arcStartPoint.x, arcStartPoint.y, arcStartPoint.z,
                                    arcEndPoint.x, arcEndPoint.y, arcEndPoint.z,
                                    -1);    // +1 : Go from the start point to the end point in a counter-clockwise direction
ClearSelection(ref modulePart);

// try to constraint the arc's starting point to the origin
SketchPoint arcStartSketchPoint = (SketchPoint)arc.GetStartPoint2();
arcStartSketchPoint.Select4(true, swSelectData);
basicRefGeometry.origin.Select4(false, swSelectData);
//SelectOrigin(ref modulePart);
MakeSelectedCoincide(ref modulePart);
ClearSelection(ref modulePart);

// Dimension the arc
((SketchSegment)arc).Select4(true, swSelectData);

AddDimensionToSelected(ref modulePart, bestFitSphereRadius, arcStartPoint.x / 2.0 + arcEndPoint.x / 2.0,
                                                            arcStartPoint.y / 2.0,
                                                            arcStartPoint.z / 2.0 + arcEndPoint.z / 2.0);

ClearSelection(ref modulePart);

// create vertical line aka the revolution axis
SketchLine revolutionAxisVerticalLine = (SketchLine)modulePart.SketchManager.CreateLine(arcStartPoint.x, arcStartPoint.y, arcStartPoint.z, 
                                                                            arcStartPoint.x, 180e-3, 0);
MakeSelectedLineVertical(ref modulePart);
ClearSelection(ref modulePart);
// try to select the origin and set the revolution axis to be coincident with it
basicRefGeometry.origin.Select4(false, swSelectData);
SketchPoint revolutionAxisVerticalLineStartPoint = (SketchPoint)revolutionAxisVerticalLine.GetStartPoint2();
revolutionAxisVerticalLineStartPoint.Select4(true, swSelectData);
MakeSelectedCoincide(ref modulePart);

ClearSelection(ref modulePart);

// coincide the center point of the arc to the revolution axis
SketchPoint arcCenterSketchPoint = (SketchPoint)arc.GetCenterPoint2();
arcCenterSketchPoint.Select4(true, swSelectData);
((SketchSegment)revolutionAxisVerticalLine).Select4(true, swSelectData);
MakeSelectedCoincide(ref modulePart);
ClearSelection(ref modulePart);

SketchPoint revolutionAxisVerticalLineEndPoint = (SketchPoint)revolutionAxisVerticalLine.GetEndPoint2();
SketchLine horizontalLine = (SketchLine)modulePart.SketchManager.CreateLine(revolutionAxisVerticalLineEndPoint.X, revolutionAxisVerticalLineEndPoint.Y, revolutionAxisVerticalLineEndPoint.Z,
                                                                            bottomSurfaceRadius, revolutionAxisVerticalLineEndPoint.Y, 0);
MakeSelectedLineHorizontal(ref modulePart);

// add dimension constraint to the horizontal line
AddDimensionToSelected(ref modulePart, bottomSurfaceRadius, revolutionAxisVerticalLineEndPoint);

ClearSelection(ref modulePart);
// create vertical line (outer rim of the boarder) connecting the top line 
SketchPoint bottomSurfaceTopRightPoint = (SketchPoint)horizontalLine.GetEndPoint2();

SketchLine revolutionAxisVerticalLineToArc = (SketchLine)modulePart.SketchManager.CreateLine(bottomSurfaceTopRightPoint.X, bottomSurfaceTopRightPoint.Y, bottomSurfaceTopRightPoint.Z, 
                                                                                bottomSurfaceTopRightPoint.X, -outerRimHeight, 0);
MakeSelectedLineVertical(ref modulePart);
ClearSelection(ref modulePart);

// make vertical line coincide with the arc
SketchPoint revolutionAxisVerticalLineToArcEndPoint = (SketchPoint)revolutionAxisVerticalLineToArc.GetEndPoint2();
revolutionAxisVerticalLineToArcEndPoint.Select4(true, swSelectData);
((SketchSegment)arc).Select4(true, swSelectData);
MakeSelectedCoincide(ref modulePart);
ClearSelection(ref modulePart);

// add dimension to outer rim height
((SketchSegment)revolutionAxisVerticalLineToArc).Select4(true, swSelectData);

AddDimensionToSelected(ref modulePart, outerRimHeight, bottomSurfaceTopRightPoint);
ClearSelection(ref modulePart);

modulePart.SketchManager.AddToDB = false;

// trim the extra arc. This is a preparation step of creating a pizza slice revolution
((SketchSegment)arc).Select4(true, swSelectData);
bool trimSuccess = modulePart.SketchManager.SketchTrim((int)swSketchTrimChoice_e.swSketchTrimClosest, arcEndPoint.x, arcEndPoint.y, arcEndPoint.z);
ClearSelection(ref modulePart);

// get the current sketch's name
string pizzaSketchName = ((Feature)modulePart.SketchManager.ActiveSketch).Name;

// quit editing sketch 
modulePart.SketchManager.InsertSketch(true);
ClearSelection(ref modulePart);

/* Create the first pizza slice */
// select the sketch to revolve with
SelectSketch(ref modulePart, pizzaSketchName, true);

// select the axis to revolve. According to API doc, we must select with a specific mark
swSelectData.Mark = 4;
((SketchSegment)revolutionAxisVerticalLine).Select4(true, swSelectData);
// Revolve the first pizza slice
// TODO: check if it's necessary to create a wrapper function for the feature revolve function. The official api takes too many parameters
Feature pizzaSlice = modulePart.FeatureManager.FeatureRevolve2(true, true, false, false, true, false, 
                                                0, 0, DegreeToRadian(60), 0, false, false, 0.01, 0.01, 0, 0, 0, true, true, true);

ClearSelection(ref modulePart);
Console.WriteLine("1/6 of the pizza created");
ZoomToFit(ref modulePart);
// enbale user input box for dimensions
EnableInputDimensionByUser(ref solidworksApp);

/* Extrude triangles on the pizza slice
 * Steps:
 * 1. create points that are both on the bottom plane and the extrusion axes    - Done
 * 2. create reference planes by using "normal and point" method                - Done
 * 3. start sketches on those planes and draw triangles on sketches             - Done
 * 4. extrude triangles                                                         - Done
 */
Console.WriteLine("Extruding modules ...");
// First define the bottom plane, by creating a parallel plane w.r.t the front plane
double bottomToFrontPlaneDistance = ((SketchSegment)revolutionAxisVerticalLine).GetLength();

// Select the front plane in a language-neutral way
basicRefGeometry.frontPlane.Select2(false, 0);

// A trick to flip the offset orientation when creating a ref plane: https://stackoverflow.com/questions/71885722/how-to-create-a-flip-offset-reference-plane-with-solidworks-vba-api
RefPlane bottomPlane = (RefPlane)modulePart.FeatureManager.InsertRefPlane((int)swRefPlaneReferenceConstraints_e.swRefPlaneReferenceConstraint_Distance 
                                                                                + (int)swRefPlaneReferenceConstraints_e.swRefPlaneReferenceConstraint_OptionFlip, bottomToFrontPlaneDistance,
                                                                             0, 0, 0, 0);
((Feature)bottomPlane).Name = "Bottom Plane";

// quick test to see if a point can be created on the bottom surface
// Create a sketch to add points on
modulePart.Insert3DSketch();
// for speed improvements
modelView.EnableGraphicsUpdate = false;
modulePart.SketchManager.AddToDB = true;

// keep a list of sketch points on the bottom plane
List<SketchPoint> bottomSurfaceSketchPointList = new( extrusionAxisList.Count );
foreach (SketchSegment extrusionAxis in extrusionAxisList)
{
    // create a point at some random location (DO NOT USE 0,0,0, that's the origin). The exact location doesn't matter since we will constraint it any ways
    SketchPoint bottomPlaneSketchPoint = modulePart.SketchManager.CreatePoint(27, 27, 27);
    bottomSurfaceSketchPointList.Add(bottomPlaneSketchPoint);

    extrusionAxis.Select4(true, swSelectData);
    MakeSelectedCoincide(ref modulePart);
    ClearSelection(ref modulePart);
    // TODO: the bottom plane will need to be passed in if this loop is used as a function
    // using -1 as the mark, meaning that we don't specify the purpose of the selection to Solidworks
    ((Feature)bottomPlane).Select2(true, -1);
    bottomPlaneSketchPoint.Select4(true, swSelectData);
    MakeSelectedCoincide(ref modulePart);
    ClearSelection(ref modulePart);
}
modelView.EnableGraphicsUpdate = true;
modulePart.SketchManager.AddToDB = false;
// close the sketch
modulePart.Insert3DSketch();
ClearSelection(ref modulePart);

/* First, create "reference sketches". These sketches will be copied and pasted to genereate all the modules
 * 1. Find the point on the bottom plane that is the closest to the origin. 
 * 2. Use InsertRefPlane Method (IFeatureManager) on this point and the extrusion axis passing through it to create a reference plane.
 */

// find the closest element 
int closestPointIdx = GetIndexSketchPointClosestToOrigin(ref bottomSurfaceSketchPointList);
SketchPoint pointClosestToOrigin = bottomSurfaceSketchPointList[closestPointIdx];

// the primisPlane has the first created full-triangle and chamfered-triangle
RefPlane primisPlane = CreateRefPlaneFromPointAndNormal(pointClosestToOrigin, extrusionAxisList[closestPointIdx], "PrimisPlane", swSelectData, modulePart.FeatureManager);
// make the first reference plane invisible to boost performance
((Feature)primisPlane).Select2(true, -1);
modulePart.BlankRefGeom();
ClearSelection(ref modulePart);

/* Create a sketch on the newly created plane and draw a triangle on it
 */
// disable user input box for dimensions. Otherwise solidworks will stuck at waiting for user inputs
DisableInputDimensionByUser(ref solidworksApp);
modulePart.SketchManager.AddToDB = true;

// Create a new sketch on a close-to-bottom plane
((Feature)primisPlane).Select2(false, -1);
modulePart.SketchManager.InsertSketch(true);

// define where the top vertix of the equilateral triangle is. 
// 0.577350269 is sqrt(3)/3
SketchPoint firstBottomSurfaceSketchPoint = bottomSurfaceSketchPointList[closestPointIdx];
Point3D topVertixTriangle = new(firstBottomSurfaceSketchPoint.X, firstBottomSurfaceSketchPoint.Y + 0.577350269 * equilateralTriangleSideLength, firstBottomSurfaceSketchPoint.Z);
object[] unchamferedTrianglePolygon = (object[])modulePart.SketchManager.CreatePolygon(firstBottomSurfaceSketchPoint.X, firstBottomSurfaceSketchPoint.Y, firstBottomSurfaceSketchPoint.Z,
                                                                            topVertixTriangle.x, topVertixTriangle.y, topVertixTriangle.z, 3, true);

ClearSelection(ref modulePart);

// constraint the center of the triangle to the extrusion axis
SketchPoint? triangleCenter = GetTriangleCenterPoint(ref unchamferedTrianglePolygon);
if (triangleCenter != null)
{
    ClearSelection(ref modulePart);
    triangleCenter.Select4(true, swSelectData);
    firstBottomSurfaceSketchPoint.Select4(true, swSelectData);
    MakeSelectedCoincide(ref  modulePart);
    ClearSelection(ref modulePart);
}

// make one of the sides horizontal - not sure if this is the best thing to do
SketchLine? oneSideOfTriangle = GetMostHorizontalTriangleSide(ref unchamferedTrianglePolygon);
if (oneSideOfTriangle != null)
{
    ClearSelection(ref modulePart);
    ((SketchSegment)oneSideOfTriangle).Select4(true, swSelectData);
    // DEBUG: add horizontal constraint on this side - maybe fine
    MakeSelectedLineHorizontal(ref modulePart);
    // Dimension the equilateral triangle's side length
    AddDimensionToSelected(ref modulePart, equilateralTriangleSideLength, firstBottomSurfaceSketchPoint);
    ClearSelection(ref modulePart);
}

// add the chamfers.
MakeChamferedTriangleFromTrianglePolygon(unchamferedTrianglePolygon, chamferLength, ref modulePart, swSelectData);
ClearSelection(ref modulePart);

// DEBUG: remember the name of the chamfered sketch
((Feature)modulePart.SketchManager.ActiveSketch).Name = "Chamfered Triangle Sketch";
string chamferedSketchName = GetActiveSketchName(ref modulePart);
// close chamfered triangle sketch
modulePart.SketchManager.InsertSketch(true);
ClearSelection(ref modulePart);

// Create a sketch on the same close-to-bottom plane for the full triangle
((Feature)primisPlane).Select2(true, -1);
modulePart.SketchManager.InsertSketch(true);

///// Done with the first chamfered triangle sketch. Now create the full triangle sketch /////
object[] fullTrianglePolygon = (object[])modulePart.SketchManager.CreatePolygon(firstBottomSurfaceSketchPoint.X, firstBottomSurfaceSketchPoint.Y, firstBottomSurfaceSketchPoint.Z,
                                                                            topVertixTriangle.x, topVertixTriangle.y, topVertixTriangle.z, 3, true);
// dimension the sides
SketchLine? oneSideOfFullTriangle = GetOneTriangleSide(ref fullTrianglePolygon);
if (oneSideOfFullTriangle != null)
{
    ClearSelection(ref modulePart);
    ((SketchSegment)oneSideOfFullTriangle).Select4(true, swSelectData);
    // Dimension the equilateral triangle's side length
    AddDimensionToSelected(ref modulePart, equilateralTriangleSideLength, (SketchPoint)oneSideOfFullTriangle.GetEndPoint2());
    ClearSelection(ref modulePart);
}
ClearSelection(ref modulePart);

// constraint the center of the full triangle to the extrusion axis
SketchPoint? fullTriangleCenter = GetTriangleCenterPoint(ref fullTrianglePolygon);
if (fullTriangleCenter != null)
{
    fullTriangleCenter.Select4(true, swSelectData);
    firstBottomSurfaceSketchPoint.Select4(true, swSelectData);
    MakeSelectedCoincide(ref modulePart);
    ClearSelection(ref modulePart);
}

// Give the first full triangle sketch a special name. 
((Feature)modulePart.SketchManager.ActiveSketch).Name = "Full Triangle Sketch";
string fullTriangleSketchName = ((Feature)modulePart.SketchManager.ActiveSketch).Name;
// quit editing sketch
modulePart.SketchManager.InsertSketch(true);
ClearSelection(ref modulePart);

/// In progress Create a sketch for the 3 pin holes ///
/// 
((Feature)primisPlane).Select2(true, -1);
modulePart.SketchManager.InsertSketch(true);
///// Done with the first chamfered triangle sketch. Now create the full triangle sketch /////
object[] pinHoleConstructionTriangle = (object[])modulePart.SketchManager.CreatePolygon(firstBottomSurfaceSketchPoint.X, firstBottomSurfaceSketchPoint.Y, firstBottomSurfaceSketchPoint.Z,
                                                                            topVertixTriangle.x, topVertixTriangle.y, topVertixTriangle.z, 3, true);
// Since the whole pin hole triangle is selected, we can directly call the API to make all of it as construction geometries
modulePart.SketchManager.CreateConstructionGeometry();
// dimension the sides
SketchLine? oneSideOfPinHoleTriangle = GetOneTriangleSide(ref pinHoleConstructionTriangle);
if (oneSideOfPinHoleTriangle != null)
{
    ClearSelection(ref modulePart);
    ((SketchSegment)oneSideOfPinHoleTriangle).Select4(true, swSelectData);
    // Dimension the equilateral triangle's side length
    AddDimensionToSelected(ref modulePart, interPinHoleDistance, (SketchPoint)oneSideOfPinHoleTriangle.GetEndPoint2());
    ClearSelection(ref modulePart);
}
// Add the pin holes on 3 vertices
// first find the vertices in a pin hole triangle
HashSet<SketchPoint> verticesInPinHoleTriangleSet = new();
foreach (SketchSegment segment in pinHoleConstructionTriangle.Cast<SketchSegment>())
{
    if (segment.GetType() == (int)swSketchSegments_e.swSketchLINE)
    {
        verticesInPinHoleTriangleSet.Add((SketchPoint)((SketchLine)segment).GetStartPoint2());
        verticesInPinHoleTriangleSet.Add((SketchPoint)((SketchLine)segment).GetEndPoint2());
    }
}
// add pin hole at every vertex
verticesInPinHoleTriangleSet.ToList().ForEach(vertex =>
{
    SketchSegment? pinHole = modulePart.SketchManager.CreateCircleByRadius(vertex.X, vertex.Y, 0, pinHoleDiameter);
    // dimension the pin hole's diammeter
    pinHole.Select4(true, swSelectData);
    // dimension the diammeter
    AddDimensionToSelected(ref modulePart, pinHoleDiameter, vertex);
    ClearSelection(ref modulePart);
});

// coincide the center point of the pin hole triangle to the extrusion axis
SketchPoint? pinHoleTriangleCenterPoint = GetPinHoleTriangleCenterPoint(ref pinHoleConstructionTriangle);
if (pinHoleTriangleCenterPoint != null)
{
    pinHoleTriangleCenterPoint.Select4(true, swSelectData);
    firstBottomSurfaceSketchPoint.Select4(true, swSelectData);
    MakeSelectedCoincide(ref modulePart);
    ClearSelection(ref modulePart);
}







// Give the first pin hole triangle sketch a special name. 
((Feature)modulePart.SketchManager.ActiveSketch).Name = "Pin Hole Triangle Sketch";
string pinHoleTriangleSketchName = ((Feature)modulePart.SketchManager.ActiveSketch).Name;
// quit editing sketch
modulePart.SketchManager.InsertSketch(true);
ClearSelection(ref modulePart);

// TODO: use for loop to create triangle modules with the right shape for all the modules
// TODO: reduce boilerplat code and move code to scaffold functions

// try to gain speed by locking the user interface
//modelView.EnableGraphicsUpdate = false;
//modulePart.SketchManager.DisplayWhenAdded = false;
// try the magic disable feature manager scroll to view to hopefully boost performance
solidworksApp.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swFeatureManagerEnsureVisible, false);
//modulePart.Lock();
for (int moduleIndex = 0; moduleIndex < bottomSurfaceSketchPointList.Count; moduleIndex++)
{
    // skip the point closest to the origin
    if (moduleIndex == closestPointIdx) 
    {
        continue;
    };

    // Create another test plane near the bottom and insert a sketch on it
    RefPlane aRefPlane = CreateRefPlaneFromPointAndNormal(bottomSurfaceSketchPointList[moduleIndex], extrusionAxisList[moduleIndex],
                                                            $"ModulePlane_{moduleIndex}", swSelectData, modulePart.FeatureManager);
    // hide the reference plane to avoid slowing down sketch creation
    ((Feature)aRefPlane).Select2(true, -1);
    modulePart.BlankRefGeom();
    ClearSelection(ref modulePart);

    // copy the chamfered triangle sketch //
    SelectSketch(ref modulePart, chamferedSketchName);
    modulePart.EditCopy();
    ((Feature)aRefPlane).Select2(true, -1);
    modulePart.Paste();
    // select the last pasted sketch
    Feature lastSketchFeature = (Feature)modulePart.FeatureByPositionReverse(0);
    lastSketchFeature.Select2(false, -1);
    Sketch lastChamferedSketch = (Sketch)lastSketchFeature.GetSpecificFeature2();
    // edit the newly created sketch to add constraints
    ((Feature)lastChamferedSketch).Select2(false, -1);
    modulePart.EditSketch();
    // record the pasted sheet's name for the extrusion later
    string pastedChamferedTriangleSheetName = GetActiveSketchName(ref modulePart);
    // make the center of the chamfered module coincident with the extrusion axis
    object[] segments = (object[])lastChamferedSketch.GetSketchSegments();
    SketchPoint? chamferedTriangleCenterPoint = GetTriangleCenterPoint(ref segments);

    // TODO: add constraints to the chamfered modules to orient the module
    if (frontGridPointCloud.moduleOrientations[moduleIndex] == false)
    {
        ClearSelection(ref modulePart);
        // orientation flag is false, meaning the module should be upside-down
        foreach(SketchSegment segment in segments.Cast<SketchSegment>())
        {
            segment.Select4(true, swSelectData);
        }
        RotateSelected(ref modulePart, chamferedTriangleCenterPoint.X, chamferedTriangleCenterPoint.Y, Math.PI);
        ClearSelection(ref modulePart);
    }

    chamferedTriangleCenterPoint?.Select4(true, swSelectData);
    bottomSurfaceSketchPointList[moduleIndex].Select4(true, swSelectData);
    MakeSelectedCoincide(ref modulePart);
    ClearSelection(ref modulePart);

    // TESTING: make one of the sides to be in parallel to the most positively sloped side of the first reference chamfered triangle
    // TODO: check if can set one side of the chamfered triangle to be in parallel with the first chamfered triangle
    //SketchSegment? unchamferedTriangleMostPositiveSlopedSide = GetMostPositiveSlopedSideInChamferedTriangle(ref segments);
    // TODO: temporary workaround, simply set one of the sides to be in horizontal to fully define each chamfered triangle
    SketchLine aRelativelyFlatSide = GetMostHorizontalTriangleSide(ref segments);
    ((SketchSegment)aRelativelyFlatSide).Select4(true, swSelectData);
    MakeSelectedLineHorizontal(ref modulePart);
    ClearSelection(ref modulePart);

    // get a handle on the longest most horizontal side of a chamfered triangle.
    // This side will be used as a reference to set parallel constraint to a side of a full triangle
    SketchLine? aLongSideChamferedTriangle = GetLongestMostHorizontalTriangleSide(ref segments);

    // quit editing sketch
    modulePart.InsertSketch2(true);
    ClearSelection(ref modulePart);
    // extrude the chamfered triangle
    SelectSketch(ref modulePart, pastedChamferedTriangleSheetName);
    Feature chamferedExtrusion = CreateTwoWayExtrusion(ref modulePart);
    chamferedExtrusion.Name = $"chamferedExtrusion_{moduleIndex}";
    ClearSelection(ref modulePart);

    /// Now make the unchamfered/full triangle extrusion ///
    // copy the full triangle sketch //
    SelectSketch(ref modulePart, fullTriangleSketchName);
    modulePart.EditCopy();
    ((Feature)aRefPlane).Select2(true, -1);
    modulePart.Paste();
    // select the last pasted full triangle sketch
    lastSketchFeature = (Feature)modulePart.FeatureByPositionReverse(0);
    lastSketchFeature.Select2(false, -1);
    Sketch lastFullTriangleSketch = (Sketch)lastSketchFeature.GetSpecificFeature2();
    // edit the newly created fully triangle sketch to add constraints
    ((Feature)lastFullTriangleSketch).Select2(false, -1);
    modulePart.EditSketch();
    // DEBUG: record the pasted sheet's name
    string pastedFullTriangleSheetName = GetActiveSketchName(ref modulePart);
    // make the center of the full triangle coincident with the extrusion axis
    object[] fullTriangleSegments = (object[])lastFullTriangleSketch.GetSketchSegments();
    SketchPoint? fullTriangleCenterPoint = GetTriangleCenterPoint(ref fullTriangleSegments);
    fullTriangleCenterPoint?.Select4(true, swSelectData);
    bottomSurfaceSketchPointList[moduleIndex].Select4(true, swSelectData);
    MakeSelectedCoincide(ref modulePart);
    ClearSelection(ref modulePart);

    // Rotate the full triangle according to the orientation flag. This step will make aligning the chamfered & full triangles very easy
    if (frontGridPointCloud.moduleOrientations[moduleIndex] == false)
    {
        // orientation flag is false, meaning the module should be upside-down
        foreach (SketchSegment fullTriangleSegment in fullTriangleSegments.Cast<SketchSegment>())
        {
            fullTriangleSegment.Select4(true, swSelectData);
        }
        RotateSelected(ref modulePart, fullTriangleCenterPoint.X, fullTriangleCenterPoint.Y, Math.PI);
        ClearSelection(ref modulePart);
    }
    // make the flattest side parallel to a flattest long side of the chamfered triangle
    // TODO: find the flattest and longest side in a chamfered triangle
    SketchLine? aLongSideFullTriangle = GetMostHorizontalTriangleSide(ref fullTriangleSegments);
    ((SketchSegment)aLongSideChamferedTriangle).Select4(true, swSelectData);
    ((SketchSegment)aLongSideFullTriangle).Select4(true, swSelectData);
    MakeSelectedLinesParallel(ref modulePart);

    // quit editing the fully triangle sketch
    modulePart.InsertSketch2(true);
    ClearSelection(ref modulePart);
    // extrude the chamfered triangle
    SelectSketch(ref modulePart, pastedFullTriangleSheetName);
    // extrude the full triangle all the way to the "support surface point"
    Feature fullTriangleExtrusion = CreateTwoWayExtrusionD1ToPointD2ThroughAll(ref modulePart, supportSurfaceMarkerPointList[moduleIndex], swSelectData);
    fullTriangleExtrusion.Name = $"fullTriangleExtrusion_{moduleIndex}";
    ClearSelection(ref modulePart);

    /// Make pin holes - first test seems fine! ///
    // copy the pin hole triangle sketch
    SelectSketch(ref modulePart, pinHoleTriangleSketchName);
    modulePart.EditCopy();
    ((Feature)aRefPlane).Select2(true, -1);
    modulePart.Paste();
    // select the last pasted pin hole triangle sketch
    lastSketchFeature = (Feature)modulePart.FeatureByPositionReverse(0);
    lastSketchFeature.Select2(false, -1);
    Sketch lastPinHoleTriangleSketch = (Sketch)lastSketchFeature.GetSpecificFeature2();
    // edit the newly created pin hole triangle sketch to add constraints
    ((Feature)lastPinHoleTriangleSketch).Select2(false, -1);
    modulePart.EditSketch();
    // record the pasted pin hole triangle's sketch name
    string pastedPinHoleTriangleSheetname = GetActiveSketchName(ref modulePart);
    // make the center of the pin hole triangle coincident with the extrusion axis
    object[] pinHoleTriangleSegments = (object[])lastPinHoleTriangleSketch.GetSketchSegments();
    SketchPoint? currentPinHoleTriangleCenterPoint = GetPinHoleTriangleCenterPoint(ref pinHoleTriangleSegments);
    currentPinHoleTriangleCenterPoint?.Select4(true, swSelectData);
    bottomSurfaceSketchPointList[moduleIndex].Select4(true, swSelectData);
    MakeSelectedCoincide(ref modulePart);
    ClearSelection(ref modulePart);

    // rotate the pin hole triangle if necessary, according to the orientation flag
    if (frontGridPointCloud.moduleOrientations[moduleIndex] == false )
    {
        // orientation flag is false, meaning the module should be upside-down
        foreach (SketchSegment pinHoleTriangleSegment in  pinHoleTriangleSegments.Cast<SketchSegment>())
        {
            pinHoleTriangleSegment.Select4(true, swSelectData);
        }
        RotateSelected(ref modulePart, pinHoleTriangleCenterPoint.X, pinHoleTriangleCenterPoint.Y, Math.PI);
        ClearSelection(ref modulePart);
    }
    // Make the flattest side parallel to the flattest side of the full triangle
    SketchLine? aLongSidePinHoleTriangle = GetMostHorizontalTriangleSide(ref pinHoleTriangleSegments);
    ((SketchSegment)aLongSidePinHoleTriangle).Select4(true, swSelectData);
    ((SketchSegment)aLongSideFullTriangle).Select4(true, swSelectData);
    MakeSelectedLinesParallel(ref modulePart);

    // quit editing the pin hole triangle sketch
    modulePart.InsertSketch2(true);
    ClearSelection(ref modulePart);
    // extrude the pin holes
    SelectSketch(ref modulePart, pastedPinHoleTriangleSheetname);
    Feature pinHoleExtrusion = CreateTwoWayExtrusion(ref modulePart);
    pinHoleExtrusion.Name = $"pinHoleExtrusion_{moduleIndex}";
    ClearSelection(ref modulePart);
}

// TODO: finally extrude the "reference sketches"

//modulePart.UnLock();
solidworksApp.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swFeatureManagerEnsureVisible, true);
modelView.EnableGraphicsUpdate = true;
modulePart.SketchManager.DisplayWhenAdded = true;
modulePart.SketchManager.AddToDB = false;

// enbale user input box for dimensions
EnableInputDimensionByUser(ref solidworksApp);

// DEBUG: print the feature tree
PrintFeaturesInFeatureManagerDesignTree(ref modulePart);

Console.WriteLine("Module extrusions completed");

// wait for user input before closing
PromptAndWait("Press any key to close Solidworks");
// close Solidworks that runs in the background
solidworksApp.ExitApp();