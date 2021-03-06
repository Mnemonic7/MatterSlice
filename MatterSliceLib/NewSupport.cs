/*
This file is part of MatterSlice. A command line utility for
generating 3D printing GCode.

Copyright (c) 2014, Lars Brubaker

MatterSlice is free software: you can redistribute it and/or modify
it under the terms of the GNU Affero General Public License as
published by the Free Software Foundation, either version 3 of the
License, or (at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU Affero General Public License for more details.

You should have received a copy of the GNU Affero General Public License
along with this program.  If not, see <http://www.gnu.org/licenses/>.
*/

using System;
using System.Collections.Generic;
using MatterHackers.Pathfinding;
using MSClipperLib;

namespace MatterHackers.MatterSlice
{
	using Polygon = List<IntPoint>;
	using Polygons = List<List<IntPoint>>;

	public class NewSupport
	{
		private static double cleanDistance_um = 10;

		public NewSupport(ConfigSettings config, List<ExtruderLayers> Extruders, ExtruderLayers userGeneratedSupport, long grabDistance_um)
		{
			cleanDistance_um = config.ExtrusionWidth_um / 10;
			long supportWidth_um = (long)(config.ExtrusionWidth_um * (100 - config.SupportPercent) / 100);
			// create starting support outlines
			List<Polygons> allPartOutlines = CalculateAllPartOutlines(config, Extruders);
			_InsetPartOutlines = CreateInsetPartOutlines(allPartOutlines, config.ExtrusionWidth_um / 2);

			int numSupportLayers = userGeneratedSupport.Layers.Count;
			SparseSupportOutlines = CreateEmptyPolygons(numSupportLayers);

			// calculate the combined outlines for everything
			for (int layerIndex = 0; layerIndex < numSupportLayers; layerIndex++)
			{
				SparseSupportOutlines[layerIndex] = userGeneratedSupport.Layers[layerIndex].AllOutlines.DeepCopy();
			}

			SparseSupportOutlines = ExpandToEasyGrabDistance(SparseSupportOutlines, grabDistance_um - supportWidth_um);

			// remove the actual parts from the support data
			SparseSupportOutlines = ClipToXyDistance(SparseSupportOutlines, _InsetPartOutlines, config);

			// create the interface layers
			InterfaceLayers = CreateInterfaceLayers(SparseSupportOutlines, config.SupportInterfaceLayers);

			// and the bottom support layers
			AirGappedBottomOutlines = CreateAirGappedBottomLayers(SparseSupportOutlines, _InsetPartOutlines);

			// remove the interface layers from the normal support layers
			SparseSupportOutlines = CalculateDifferencePerLayer(SparseSupportOutlines, InterfaceLayers);
			// remove the airGappedBottomOutlines layers from the normal support layers
			SparseSupportOutlines = CalculateDifferencePerLayer(SparseSupportOutlines, AirGappedBottomOutlines);
		}

		//List<Polygons> pushedUpTopOutlines = new List<Polygons>();
		public List<Polygons> AirGappedBottomOutlines { get; }

		public List<Polygons> InterfaceLayers { get; }
		public List<Polygons> SparseSupportOutlines { get; }

		#region // unit testing data

		public List<Polygons> _InsetPartOutlines { get; }

		#endregion // unit testing data

		public Polygons GetBedOutlines()
		{
			return SparseSupportOutlines[0].CreateUnion(InterfaceLayers[0]);
		}

		public Polygons GetRequiredSupportAreas(int layerIndex)
		{
			layerIndex--;
			if (layerIndex < InterfaceLayers.Count && layerIndex >= 0)
			{
				if (InterfaceLayers[layerIndex].Count > 0)
				{
					return InterfaceLayers[layerIndex];
				}
				else if (layerIndex < SparseSupportOutlines.Count)
				{
					return SparseSupportOutlines[layerIndex];
				}
			}

			return new Polygons();
		}

		public bool HasInterfaceSupport(int layerIndex)
		{
			return InterfaceLayers[layerIndex].Count > 0;
		}

		public bool HasNormalSupport(int layerIndex)
		{
			return SparseSupportOutlines[layerIndex].Count > 0;
		}

		public void QueueAirGappedBottomLayer(ConfigSettings config, LayerGCodePlanner gcodeLayer, int layerIndex, GCodePathConfig supportNormalConfig)
		{
			// normal support
			Polygons currentAirGappedBottoms = AirGappedBottomOutlines[layerIndex];
			currentAirGappedBottoms = currentAirGappedBottoms.Offset(-config.ExtrusionWidth_um / 2);
			List<Polygons> supportIslands = currentAirGappedBottoms.ProcessIntoSeparateIslands();

			foreach (Polygons islandOutline in supportIslands)
			{
				// force a retract if changing islands
				if (config.RetractWhenChangingIslands)
				{
					gcodeLayer.ForceRetract();
				}

				Polygons islandInfillLines = new Polygons();
				// render a grid of support
				if (config.GenerateSupportPerimeter)
				{
					Polygons outlines = Clipper.CleanPolygons(islandOutline, config.ExtrusionWidth_um / 4);
					gcodeLayer.QueuePolygonsByOptimizer(outlines, null, supportNormalConfig, 0);
				}
				Polygons infillOutline = islandOutline.Offset(-config.ExtrusionWidth_um / 2);
				switch (config.SupportType)
				{
					case ConfigConstants.SUPPORT_TYPE.GRID:
						Infill.GenerateGridInfill(config, infillOutline, islandInfillLines, config.SupportInfillStartingAngle, config.SupportLineSpacing_um);
						break;

					case ConfigConstants.SUPPORT_TYPE.LINES:
						Infill.GenerateLineInfill(config, infillOutline, islandInfillLines, config.SupportInfillStartingAngle, config.SupportLineSpacing_um);
						break;
				}
				gcodeLayer.QueuePolygonsByOptimizer(islandInfillLines, null, supportNormalConfig, 0);
			}
		}

		public bool QueueInterfaceSupportLayer(ConfigSettings config, LayerGCodePlanner gcodeLayer, int layerIndex, GCodePathConfig supportInterfaceConfig)
		{
			// interface
			bool outputPaths = false;
			Polygons currentInterfaceOutlines2 = InterfaceLayers[layerIndex].Offset(-config.ExtrusionWidth_um / 2);
			if (currentInterfaceOutlines2.Count > 0)
			{
				List<Polygons> interfaceIslands = currentInterfaceOutlines2.ProcessIntoSeparateIslands();

				foreach (Polygons interfaceOutline in interfaceIslands)
				{
					// force a retract if changing islands
					if (config.RetractWhenChangingIslands)
					{
						gcodeLayer.ForceRetract();
					}

					// make a border if layer 0
					if (layerIndex == 0)
					{
						Polygons infillOutline = interfaceOutline.Offset(-supportInterfaceConfig.lineWidth_um / 2);
						Polygons outlines = Clipper.CleanPolygons(infillOutline, config.ExtrusionWidth_um / 4);
						if (gcodeLayer.QueuePolygonsByOptimizer(outlines, null, supportInterfaceConfig, 0))
						{
							outputPaths = true;
						}
					}

					Polygons supportLines = new Polygons();
					Infill.GenerateLineInfill(config, interfaceOutline, supportLines, config.InfillStartingAngle + 90, config.ExtrusionWidth_um);
					if (gcodeLayer.QueuePolygonsByOptimizer(supportLines, null, supportInterfaceConfig, 0))
					{
						outputPaths = true;
					}
				}
			}

			return outputPaths;
		}

		public bool QueueNormalSupportLayer(ConfigSettings config, LayerGCodePlanner gcodeLayer, int layerIndex, GCodePathConfig supportNormalConfig)
		{
			// normal support
			Polygons currentSupportOutlines = SparseSupportOutlines[layerIndex];
			currentSupportOutlines = currentSupportOutlines.Offset(-supportNormalConfig.lineWidth_um / 2);
			List<Polygons> supportIslands = currentSupportOutlines.ProcessIntoSeparateIslands();

			bool outputPaths = false;
			foreach (Polygons islandOutline in supportIslands)
			{
				// force a retract if changing islands
				if (config.RetractWhenChangingIslands)
				{
					gcodeLayer.ForceRetract();
				}

				Polygons islandInfillLines = new Polygons();
				// render a grid of support
				if (config.GenerateSupportPerimeter || layerIndex == 0)
				{
					Polygons outlines = Clipper.CleanPolygons(islandOutline, config.ExtrusionWidth_um / 4);
					if (gcodeLayer.QueuePolygonsByOptimizer(outlines, null, supportNormalConfig, 0))
					{
						outputPaths = true;
					}
				}

				Polygons infillOutline = islandOutline.Offset(-(int)supportNormalConfig.lineWidth_um);

				if (layerIndex == 0)
				{
					// on the first layer print this as solid
					Infill.GenerateLineInfill(config, infillOutline, islandInfillLines, config.SupportInfillStartingAngle, config.ExtrusionWidth_um);
				}
				else
				{
					switch (config.SupportType)
					{
						case ConfigConstants.SUPPORT_TYPE.GRID:
							Infill.GenerateGridInfill(config, infillOutline, islandInfillLines, config.SupportInfillStartingAngle, config.SupportLineSpacing_um);
							break;

						case ConfigConstants.SUPPORT_TYPE.LINES:
							Infill.GenerateLineInfill(config, infillOutline, islandInfillLines, config.SupportInfillStartingAngle, config.SupportLineSpacing_um);
							break;
					}
				}

				PathFinder pathFinder = null;
				if (config.AvoidCrossingPerimeters)
				{
					pathFinder = new PathFinder(infillOutline, -config.ExtrusionWidth_um / 2, useInsideCache: config.AvoidCrossingPerimeters);
				}
				var oldPathFinder = gcodeLayer.PathFinder;
				gcodeLayer.PathFinder = pathFinder;
				if (gcodeLayer.QueuePolygonsByOptimizer(islandInfillLines, null, supportNormalConfig, 0))
				{
					outputPaths |= true;
				}
				gcodeLayer.PathFinder = oldPathFinder;
			}

			return outputPaths;
		}

		private static List<Polygons> CalculateAllPartOutlines(ConfigSettings config, List<ExtruderLayers> Extruders)
		{
			int numLayers = Extruders[0].Layers.Count;

			List<Polygons> allPartOutlines = CreateEmptyPolygons(numLayers);

			foreach (var extruder in Extruders)
			{
				// calculate the combined outlines for everything
				for (int layerIndex = numLayers - 1; layerIndex >= 0; layerIndex--)
				{
					allPartOutlines[layerIndex] = allPartOutlines[layerIndex].CreateUnion(extruder.Layers[layerIndex].AllOutlines);
				}
			}

			return allPartOutlines;
		}

		private static List<Polygons> CalculateDifferencePerLayer(List<Polygons> inputPolys, List<Polygons> outlinesToRemove)
		{
			int numLayers = inputPolys.Count;

			List<Polygons> diferenceLayers = CreateEmptyPolygons(numLayers);
			for (int layerIndex = numLayers - 2; layerIndex >= 0; layerIndex--)
			{
				Polygons curRequiredSupport = inputPolys[layerIndex];
				Polygons totalSupportThisLayer = curRequiredSupport.CreateDifference(outlinesToRemove[layerIndex]);

				diferenceLayers[layerIndex] = Clipper.CleanPolygons(totalSupportThisLayer, cleanDistance_um);
			}

			return diferenceLayers;
		}

		private static List<Polygons> ClipToXyDistance(List<Polygons> inputPolys, List<Polygons> allPartOutlines, ConfigSettings config)
		{
			int numLayers = inputPolys.Count;

			List<Polygons> clippedToXyOutlines = CreateEmptyPolygons(numLayers);
			for (int layerIndex = numLayers - 2; layerIndex >= 0; layerIndex--)
			{
				Polygons curRequiredSupport = inputPolys[layerIndex];
				Polygons expandedlayerPolys = allPartOutlines[layerIndex].Offset(config.SupportXYDistance_um);
				Polygons totalSupportThisLayer = curRequiredSupport.CreateDifference(expandedlayerPolys);

				clippedToXyOutlines[layerIndex] = Clipper.CleanPolygons(totalSupportThisLayer, cleanDistance_um);
			}

			return clippedToXyOutlines;
		}

		private static List<Polygons> CreateAirGappedBottomLayers(List<Polygons> inputPolys, List<Polygons> allPartOutlines)
		{
			int numLayers = inputPolys.Count;

			List<Polygons> airGappedBottoms = CreateEmptyPolygons(numLayers);
			for (int layerIndex = 1; layerIndex < numLayers; layerIndex++)
			{
				Polygons belowOutlines = allPartOutlines[layerIndex - 1];

				Polygons curRequiredSupport = inputPolys[layerIndex];
				Polygons airGapArea = belowOutlines.CreateIntersection(curRequiredSupport);

				airGappedBottoms[layerIndex] = airGapArea;
			}

			return airGappedBottoms;
		}

		private static List<Polygons> CreateEmptyPolygons(int numLayers)
		{
			List<Polygons> polygonsList = new List<Polygons>();
			for (int i = 0; i < numLayers; i++)
			{
				polygonsList.Add(new Polygons());
			}

			return polygonsList;
		}

		private static List<Polygons> CreateInsetPartOutlines(List<Polygons> inputPolys, long insetAmount_um)
		{
			int numLayers = inputPolys.Count;
			List<Polygons> allInsetOutlines = CreateEmptyPolygons(numLayers);
			// calculate all the non-supported areas
			for (int layerIndex = 0; layerIndex < numLayers; layerIndex++)
			{
				Polygons insetPolygons = Clipper.CleanPolygons(inputPolys[layerIndex].Offset(-insetAmount_um), cleanDistance_um);
				List<Polygons> insetIslands = insetPolygons.ProcessIntoSeparateIslands();

				foreach (Polygons insetOutline in insetIslands)
				{
					insetOutline.RemoveSmallAreas(insetAmount_um * 2);
					foreach (var islandPart in insetOutline)
					{
						allInsetOutlines[layerIndex].Add(islandPart);
					}
				}
			}

			return allInsetOutlines;
		}

		private static List<Polygons> CreateInterfaceLayers(List<Polygons> inputPolys, int numInterfaceLayers)
		{
			int numLayers = inputPolys.Count;

			List<Polygons> allInterfaceLayers = CreateEmptyPolygons(numLayers);
			if (numInterfaceLayers > 0)
			{
				for (int layerIndex = 0; layerIndex < numLayers; layerIndex++)
				{
					Polygons requiredInterfacePolys = inputPolys[layerIndex].DeepCopy();

					if (layerIndex < numLayers- 1)
					{
						Polygons intersectionsAbove = inputPolys[layerIndex + 1].DeepCopy();

						for (int aboveIndex = layerIndex + 2; aboveIndex < Math.Min(layerIndex + numInterfaceLayers + 1, numLayers); aboveIndex++)
						{
							intersectionsAbove = intersectionsAbove.CreateIntersection(inputPolys[aboveIndex]);
							intersectionsAbove = Clipper.CleanPolygons(intersectionsAbove, cleanDistance_um);
						}

						requiredInterfacePolys = requiredInterfacePolys.CreateDifference(intersectionsAbove);
						requiredInterfacePolys = Clipper.CleanPolygons(requiredInterfacePolys, cleanDistance_um);
					}

					allInterfaceLayers[layerIndex] = requiredInterfacePolys;
				}
			}

			return allInterfaceLayers;
		}

		private static List<Polygons> ExpandToEasyGrabDistance(List<Polygons> inputPolys, long grabDistance_um)
		{
			int numLayers = inputPolys.Count;

			List<Polygons> easyGrabDistanceOutlines = CreateEmptyPolygons(numLayers);
			for (int layerIndex = numLayers - 1; layerIndex >= 0; layerIndex--)
			{
				Polygons curLayerPolys = inputPolys[layerIndex];
				easyGrabDistanceOutlines[layerIndex] = Clipper.CleanPolygons(curLayerPolys.Offset(grabDistance_um), cleanDistance_um);
			}

			return easyGrabDistanceOutlines;
		}
	}
}
