﻿// See https://aka.ms/new-console-template for more information

using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidworksAutomationTool;
using System.Diagnostics;
using static SolidworksAutomationTool.ScaffoldFunctions;

Console.WriteLine("Welcome to the LASTRO Solidworks Automation Tool!");

// import the point cloud (the grid text file generated by the python script)
/* Uncomment the Console.ReadLine() to restore normal path input. Currently they are commented out for debug purpose */
Console.WriteLine("Please enter the path of the FRONT grid point cloud txt file");
//string frontGridPointCloudFilePath = Console.ReadLine();
string frontGridPointCloudFilePath = Path.GetFullPath(@"..\..\..\..\..\Results_examples\front_grid_indiv_102.txt"); // For debug use.

Console.WriteLine("Reading front grid point cloud file ...");
PointCloud frontGridPointCloud = new();
frontGridPointCloud.ReadPointCloudFromTxt(frontGridPointCloudFilePath, Units.Millimeter);
Console.WriteLine("Completed reading point cloud file");

Console.WriteLine("Please enter the path of the BACK grid point cloud txt file");
//string backGridPointCloudFilePath = Console.ReadLine();
string backGridPointCloudFilePath = Path.GetFullPath(@"..\..\..\..\..\Results_examples\back_grid_indiv_102.txt"); // For debug use

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
// TODO: GUI aspect: make the sphere's radius a variable
const double bestFitSphereRadius = 11045.6e-3;    // in meters

// Using the add operation because the z coordinates in the point clouds are negative. We want to offset them to close to zero
foreach ((Point3D frontPoint, Point3D backPoint) in frontGridPointCloud.point3Ds.Zip(backGridPointCloud.point3Ds))
{
    frontPoint.z += bestFitSphereRadius;
    backPoint.z += bestFitSphereRadius;
}

// DEBUG use: check if the points are read in correctly
Console.WriteLine("\nFront grid point cloud with z-axis offset removed: ");
frontGridPointCloud.PrintPoint3Ds();

// DEBUG use: check if the points are read in correctly
Console.WriteLine("\nBack grid point cloud with z-axis offset removed: ");
backGridPointCloud.PrintPoint3Ds();

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

// create a part
modulePart = solidworksApp.INewDocument2( solidworksApp.GetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swDefaultTemplatePart), 0, 0, 0);

//PromptAndWait("Press any key to insert 3D sketch");
modulePart.SketchManager.Insert3DSketch(true);

// set the view to isometric. The empty string tells solidworks to use the view indicated by the swStandardViews_e enum.
modulePart.ShowNamedView2("", (int)swStandardViews_e.swIsometricView);

// disable user input box when adding dimensions
solidworksApp.SetUserPreferenceToggle( (int)swUserPreferenceToggle_e.swInputDimValOnCreate, false );
// disable view refreshing until points are created
modelView =(ModelView)modulePart.GetFirstModelView();
modelView.EnableGraphicsUpdate = false;
modulePart.SketchManager.AddToDB = true;

// try diabling feature tree updates to gain performance
modulePart.FeatureManager.EnableFeatureTree = false;

// try to allocate space for front sketchpoints and back sketchpoints
List<SketchPoint> frontSketchPointList = new List<SketchPoint>(frontGridPointCloud.point3Ds.Count);
List<SketchPoint> backSketchPointList = new List<SketchPoint>(backGridPointCloud.point3Ds.Count);
List<SketchSegment> extrusionAxisList = new List<SketchSegment>(frontSketchPointList.Count);

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

// According to solidworks api, we need to define a SelectData object and pass it into each selection call.
SelectionMgr swSelectionManager = (SelectionMgr)modulePart.SelectionManager;
SelectData swSelectData = swSelectionManager.CreateSelectData();

// EnableGraphicsUpdate affects whether to refresh the model view during a selection, such as IEntity::Select4 or IFeature::Select2.
modelView.EnableGraphicsUpdate = false;

// Define small segment length
double smallSegmentLength = 30e-3; // in meters

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

    AddDimensionToSelected(ref modulePart, smallSegmentLength, frontSketchPoint);

    ClearSelection(ref modulePart);
}
// enbale user input box for dimensions
solidworksApp.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swInputDimValOnCreate, true);

// restore settings to make solidworks operate as normal
modulePart.SketchManager.AddToDB = false;
modelView.EnableGraphicsUpdate = true;
modulePart.FeatureManager.EnableFeatureTree = true;

modulePart.SketchManager.Insert3DSketch(true);
ClearSelection(ref modulePart);
ZoomToFit(ref modulePart);

modulePart.SketchManager.AddToDB = true;

PromptAndWait("Press any key to revolve a pizza slice");

// define variables needed for pizza creation
double arcAngle = DegreeToRadian(15);

// TODO: verify Solidworks wants points to be defined in the local cartesian coordinate frame.
Point3D arcCenterPoint = new(0, -bestFitSphereRadius, 0);
Point3D arcStartPoint = new(0, 0, 0);
Point3D arcEndPoint = new(bestFitSphereRadius * Math.Sin(arcAngle), -bestFitSphereRadius * (1 - Math.Cos(arcAngle)), 0);

modulePart.Extension.SelectByID2("Top Plane", "PLANE", 0, 0, 0, false, 0, null, 0);
// create arc to form the curved top surface
modulePart.SketchManager.InsertSketch(true);

// TODO: find a good way to describe which line is which
solidworksApp.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swInputDimValOnCreate, false);

SketchArc arc = (SketchArc)modulePart.SketchManager.CreateArc(arcCenterPoint.x, arcCenterPoint.y, arcCenterPoint.z,
                                    arcStartPoint.x, arcStartPoint.y, arcStartPoint.z,
                                    arcEndPoint.x, arcEndPoint.y, arcEndPoint.z,
                                    -1);    // +1 : Go from the start point to the end point in a counter-clockwise direction
ClearSelection(ref modulePart);

// try to constraint the arc's starting point to the origin
SketchPoint arcStartSketchPoint = (SketchPoint)arc.GetStartPoint2();
arcStartSketchPoint.Select4(true, swSelectData);
SelectOrigin(ref modulePart);
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
SelectOrigin(ref modulePart);
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

// create horizontal line (for flat bottom surface)
double bottomSurfaceRadius = 663.27e-3;
SketchPoint revolutionAxisVerticalLineEndPoint = (SketchPoint)revolutionAxisVerticalLine.GetEndPoint2();
SketchLine horizontalLine = (SketchLine)modulePart.SketchManager.CreateLine(revolutionAxisVerticalLineEndPoint.X, revolutionAxisVerticalLineEndPoint.Y, revolutionAxisVerticalLineEndPoint.Z,
                                                                            bottomSurfaceRadius, revolutionAxisVerticalLineEndPoint.Y, 0);
MakeSelectedLineHorizontal(ref modulePart);

// add dimension constraint to the horizontal line
AddDimensionToSelected(ref modulePart, bottomSurfaceRadius, revolutionAxisVerticalLineEndPoint);

ClearSelection(ref modulePart);
// create vertical line (outer rim of the boarder) connecting the top line 
SketchPoint bottomSurfaceTopRightPoint = (SketchPoint)horizontalLine.GetEndPoint2();
double outerRimHeight = 200e-3;
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

// DEBUG: try to get the current sketch's name
string pizzaSketchName = ((Feature)modulePart.SketchManager.ActiveSketch).Name;

// quit editing sketch 
modulePart.SketchManager.InsertSketch(true);
ClearSelection(ref modulePart);

/* Create the first pizza slice */
// select the sketch to use
// TODO: possibly create a wrapper function to select sketches and lines
modulePart.Extension.SelectByID2(pizzaSketchName, "EXTSKETCH", 0, 0, 0, true, 0, null, 0);

// select the axis to revolve. According to API doc, we must select with a specific mark
swSelectData.Mark = 4;
((SketchSegment)revolutionAxisVerticalLine).Select4(true, swSelectData);
// Revolve the first pizza slice
// TODO: check if it's necessary to create a wrapper function for the feature revolve function. The official api takes too many parameters
Feature pizzaSlice = modulePart.FeatureManager.FeatureRevolve2(true, true, false, false, true, false, 
                                                0, 0, DegreeToRadian(60), 0, false, false, 0.01, 0.01, 0, 0, 0, true, true, true);

ClearSelection(ref modulePart);

ZoomToFit(ref modulePart);
// enbale user input box for dimensions
solidworksApp.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swInputDimValOnCreate, true);

// TODO: write a function to extrude one triangle. By using this function repetitively, the program remains clean and easy-to-maintain
/* Extrude triangles in the slice
 * Steps:
 * 1. create points that are both on the bottom plane and the extrusion axes    - Done
 * 2. create reference planes by using "normal and point" method
 * 3. start sketches on those planes and draw triangles on sketches
 * 4. extrude triangles
 */
// First define the bottom plane, by creating a parallel plane w.r.t the front plane
double bottomToFrontPlaneDistance = ((SketchSegment)revolutionAxisVerticalLine).GetLength();
modulePart.Extension.SelectByID2("Front Plane", "PLANE", 0, 0, 0, false, 0, null, 0);
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

/* Create planes near the bottom plane      - looking good!
 * Steps:
 * 1. Try InsertRefPlane Method (IFeatureManager)
 * 2. select a point on the bottom plane, require it to be coincident with the ref plane
 * 3. select the extrusion axis, require it to be perpendicular to the ref plane
 */        
ClearSelection(ref modulePart);
RefPlane aBottomRefPlane = CreateRefPlaneFromPointAndNormal(bottomSurfaceSketchPointList[0], extrusionAxisList[0], swSelectData, modulePart.FeatureManager);
((Feature)aBottomRefPlane).Name = "A Test Plane";
ClearSelection(ref modulePart);

/* Create a sketch on the newly created plane and draw a triangle on it
 */
// disable user input box for dimensions. Otherwise solidworks will stuck at waiting for user inputs
solidworksApp.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swInputDimValOnCreate, false);
modulePart.SketchManager.AddToDB = true;

((Feature)aBottomRefPlane).Select2(true, -1);
modulePart.SketchManager.InsertSketch(true);

// define where the top vertix of the equilateral triangle is.
// 0.577350269 is sqrt(3)/3
// each side of the triangle is 74.5mm long
double equilateralTriangleSideLength = 74.5e-3;
SketchPoint someSketchPoint = bottomSurfaceSketchPointList[0];
Point3D topVertixTriangle = new(someSketchPoint.X, someSketchPoint.Y + 0.577350269 * equilateralTriangleSideLength, someSketchPoint.Z);
object[] trianglePolygon = (object[])modulePart.SketchManager.CreatePolygon(someSketchPoint.X, someSketchPoint.Y, someSketchPoint.Z,
                                                                            topVertixTriangle.x, topVertixTriangle.y, topVertixTriangle.z, 3, true);

ClearSelection(ref modulePart);

// constraint the center of the triangle to the extrusion axis
SketchPoint? triangleCenter = GetTriangleCenterPoint(ref trianglePolygon);
if (triangleCenter != null)
{
    ClearSelection(ref modulePart);
    triangleCenter.Select4(true, swSelectData);
    someSketchPoint.Select4(true, swSelectData);
    MakeSelectedCoincide(ref  modulePart);
    ClearSelection(ref modulePart);
}

// make one of the sides horizontal
SketchLine? oneSideOfTriangle = GetOneTriangleSide(ref trianglePolygon);
if (oneSideOfTriangle != null)
{
    ClearSelection(ref modulePart);
    ((SketchSegment)oneSideOfTriangle).Select4(true, swSelectData);
    // Dimension the equilateral triangle's side length
    AddDimensionToSelected(ref modulePart, equilateralTriangleSideLength, someSketchPoint);
    ClearSelection(ref modulePart);
}

// make a block out of the triangle. NOTE: for some reasons, MultiSelect2 Method (IModelDocExtension) does NOT select anything in the triangle polygon
SketchBlockDefinition chamferedTriangleBlock = MakeChamferedTriangleBlockFromTrianglePolygon(trianglePolygon, 10.5e-3, ref modulePart, swSelectData);

//  save the block to disk - tested, fine
bool saveChamferedTriangleSuccess = chamferedTriangleBlock.Save(Path.GetFullPath(@"..\..\..\chamferedTriangle.sldblk"));
if (saveChamferedTriangleSuccess)
{
    Debug.WriteLine("Save chamfered triangle successfully");
}

ClearSelection(ref modulePart);
modulePart.SketchManager.InsertSketch(true);
ClearSelection(ref modulePart);

((Feature)aBottomRefPlane).Select2(true, -1);
modulePart.SketchManager.InsertSketch(true);

//PromptAndWait("Press any key to make a full triangle block");

// Now make only the triangle shape then save it as a block
object[] fullTrianglePolygon = (object[])modulePart.SketchManager.CreatePolygon(someSketchPoint.X, someSketchPoint.Y, someSketchPoint.Z,
                                                                            topVertixTriangle.x, topVertixTriangle.y, topVertixTriangle.z, 3, true);
// dimension the sides
oneSideOfTriangle = GetOneTriangleSide(ref fullTrianglePolygon);
if (oneSideOfTriangle != null)
{
    ClearSelection(ref modulePart);
    ((SketchSegment)oneSideOfTriangle).Select4(true, swSelectData);
    // Dimension the equilateral triangle's side length
    AddDimensionToSelected(ref modulePart, equilateralTriangleSideLength, (SketchPoint)oneSideOfTriangle.GetStartPoint2());
    ClearSelection(ref modulePart);
}

SketchBlockDefinition triangleBlock = MakeTriangleBlockFromTrianglePolygon(fullTrianglePolygon, ref modulePart, swSelectData);
// save the triangle block to disk - tested, fine
bool triangleBlockSaveSuccess = triangleBlock.Save( Path.GetFullPath(@"..\..\..\fullTriangle.sldblk"));
if (triangleBlockSaveSuccess)
{
    Debug.WriteLine("Save full triangle successfully");
}

ClearSelection(ref modulePart);

modulePart.SketchManager.AddToDB = false;

// DEBUG: testing selection of segments inside a block - seems fine
object[] segmentsInTriangle = (object[])triangleBlock.GetSketch().GetSketchSegments();
segmentsInTriangle.ToList().ForEach( segment => ((SketchSegment)segment).Select4(true, swSelectData));

PromptAndWait("Waiting");

// enbale user input box for dimensions
solidworksApp.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swInputDimValOnCreate, true);

modulePart.SketchManager.InsertSketch(true);

ClearSelection(ref modulePart);

// wait for user input before closing
PromptAndWait("Press any key to close Solidworks");

// close Solidworks that runs in the background
solidworksApp.ExitApp();