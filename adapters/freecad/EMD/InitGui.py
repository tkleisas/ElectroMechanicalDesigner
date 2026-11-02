"""FreeCAD GUI workbench for exporting EMD manifests."""

import os
import sys

import FreeCAD
import FreeCADGui

sys.path.insert(0, os.path.dirname(__file__))

from emd_export import export_active_document


class ExportManifestCommand:
    def GetResources(self):
        return {
            "MenuText": "Export EMD manifest",
            "ToolTip": "Export parts, connectors, materials, BOM, and STEP/STL artifacts",
        }

    def Activated(self):
        path = export_active_document()
        if path:
            FreeCAD.Console.PrintMessage(f"EMD manifest: {path}\n")
        else:
            FreeCAD.Console.PrintWarning("EMD export cancelled\n")


FreeCADGui.addCommand("EMD_ExportManifest", ExportManifestCommand())


class EMDWorkbench(FreeCADGui.Workbench):
    MenuText = "EMD"
    ToolTip = "ElectroMechanicalDesigner export tools"

    def Initialize(self):
        self.appendToolbar("EMD", ["EMD_ExportManifest"])
        self.appendMenu("EMD", ["EMD_ExportManifest"])

    def Activated(self):
        pass

    def Deactivated(self):
        pass

    def GetClassName(self):
        return "Gui::PythonWorkbench"


FreeCADGui.addWorkbench(EMDWorkbench())
