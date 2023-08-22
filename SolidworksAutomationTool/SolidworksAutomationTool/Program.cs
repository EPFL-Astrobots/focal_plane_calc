﻿// See https://aka.ms/new-console-template for more information

using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidworksAutomationTool;

/* Display a prompt to the console and wait for the user's input before continuing */
static void PromptAndWait(string prompt)
{
    Console.WriteLine(prompt);
    Console.ReadKey();
}
Console.WriteLine("Welcome to the LASTRO Solidworks Automation Tool!");

// import the point cloud (the grid text file generated by the python script)
Console.WriteLine("Please enter the path of the FRONT grid point cloud txt file");
string frontGridPointCloudFilePath = Console.ReadLine();

Console.WriteLine("Reading front grid point cloud file ...");
PointCloud frontGridPointCloud = new();
frontGridPointCloud.ReadPointCloudFromTxt(frontGridPointCloudFilePath, Units.Millimeter);
Console.WriteLine("Completed reading point cloud file");

// DEBUG use: check if the points are read in correctly
Console.WriteLine("\nFront grid point cloud: ");
frontGridPointCloud.PrintPoint3Ds();

Console.WriteLine("Please enter the path of the BACK grid point cloud txt file");
string backGridPointCloudFilePath = Console.ReadLine();

Console.WriteLine("Reading back grid point cloud file ...");
PointCloud backGridPointCloud = new();
backGridPointCloud.ReadPointCloudFromTxt(backGridPointCloudFilePath, Units.Millimeter);
Console.WriteLine("Completed reading point cloud file");

// DEBUG use: check if the points are read in correctly
Console.WriteLine("\nBack grid point cloud: ");
backGridPointCloud.PrintPoint3Ds();

if ( frontGridPointCloud.point3Ds.Count != backGridPointCloud.point3Ds.Count )
{
    Console.WriteLine("WARNING: the number of points on the front and back grid are not the same. Is this intentional?");
}

Console.WriteLine("Starting SolidWorks Application ...");

SldWorks? solidworksApp;
ModelDoc2 modulePart;

// get solidworks and start it
const string solidWorkAppID = "SldWorks.Application";
solidworksApp = Activator.CreateInstance(Type.GetTypeFromProgID(solidWorkAppID)) as SldWorks;

if (solidworksApp == null)
{
    Console.WriteLine("SolidWorks could not be started. Exiting program now");
    return;
}

solidworksApp.Visible = true;
Console.WriteLine("SolidWorks should appear. If not, there is an error starting solidworks");

// create part file. Seems fine
PromptAndWait("Press any key to create part");

/* Start modeling */
// create a part
modulePart = solidworksApp.INewDocument2( solidworksApp.GetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swDefaultTemplatePart), 0, 0, 0);

PromptAndWait("Press any key to insert 3D sketch");
modulePart.SketchManager.Insert3DSketch(true);

// TODO: figure out how to set the view to isometric

PromptAndWait("Press any key to create point clouds and axes of extrusions");

// Try iterating through two point clouds at the same time
foreach ( (Point3D frontPoint, Point3D backPoint) in frontGridPointCloud.point3Ds.Zip(backGridPointCloud.point3Ds))
{
    modulePart.SketchManager.CreatePoint(frontPoint.x, frontPoint.y , frontPoint.z );
    modulePart.SketchManager.CreatePoint(backPoint.x , backPoint.y  , backPoint.z );
    // create axis of extrusion as construction lines
    modulePart.SketchManager.CreateLine(frontPoint.x , frontPoint.y , frontPoint.z, backPoint.x, backPoint.y, backPoint.z).ConstructionGeometry = true;
}

// TODO: find best way to select point pairs

// The documentation says: Inserts a new 3D sketch in a model or closes the active sketch. ?? 
modulePart.SketchManager.Insert3DSketch(true);

// wait for user input before closing
PromptAndWait("Press any key to close Solidworks");

// close Solidworks that runs in the background
solidworksApp.ExitApp();