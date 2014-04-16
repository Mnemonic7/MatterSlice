/*
Copyright (C) 2013 David Braam
Copyright (c) 2014, Lars Brubaker

This file is part of MatterSlice.

MatterSlice is free software: you can redistribute it and/or modify
it under the terms of the GNU Lesser General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

MatterSlice is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with MatterSlice.  If not, see <http://www.gnu.org/licenses/>.
*/

using System;
using System.Collections.Generic;
using System.Diagnostics;

using MatterSlice.ClipperLib;

namespace MatterHackers.MatterSlice
{
    using Polygon = List<IntPoint>;
    using Polygons = List<List<IntPoint>>;

    public static class Infill
    {
        public static void GenerateLinePaths(Polygons in_outline, Polygons result, int extrusionWidth_um, int lineSpacing, int infillExtendIntoPerimeter_um, double rotation)
        {
            Polygons outlines = in_outline.Offset(infillExtendIntoPerimeter_um);
            PointMatrix matrix = new PointMatrix(rotation);

            outlines.applyMatrix(matrix);

            AABB boundary = new AABB(outlines);

            boundary.min.X = ((boundary.min.X / lineSpacing) - 1) * lineSpacing;
            int lineCount = (int)((boundary.max.X - boundary.min.X + (lineSpacing - 1)) / lineSpacing);
            List<List<long>> cutList = new List<List<long>>();
            for (int n = 0; n < lineCount; n++)
            {
                cutList.Add(new List<long>());
            }

            for (int outlineIndex = 0; outlineIndex < outlines.Count; outlineIndex++)
            {
                Polygon currentOutline = outlines[outlineIndex];
                IntPoint previousPoint = currentOutline[currentOutline.Count - 1];
                for (int pointIndex = 0; pointIndex < currentOutline.Count; pointIndex++)
                {
                    IntPoint currentPoint = currentOutline[pointIndex];
                    int idx0 = (int)((currentPoint.X - boundary.min.X) / lineSpacing);
                    int idx1 = (int)((previousPoint.X - boundary.min.X) / lineSpacing);
                    
                    long xMin = Math.Min(currentPoint.X, previousPoint.X);
                    long xMax = Math.Max(currentPoint.X, previousPoint.X);

                    if (currentPoint.X > previousPoint.X)
                    {
                        xMin = previousPoint.X; 
                        xMax = currentPoint.X; 
                    }

                    if (idx0 > idx1) 
                    {
                        int tmp = idx0; 
                        idx0 = idx1; 
                        idx1 = tmp; 
                    }

                    for (int idx = idx0; idx <= idx1; idx++)
                    {
                        int x = (int)((idx * lineSpacing) + boundary.min.X + lineSpacing / 2);
                        if (x < xMin || x >= xMax)
                        {
                            continue;
                        }

                        int y = (int)(currentPoint.Y + (previousPoint.Y - currentPoint.Y) * (x - currentPoint.X) / (previousPoint.X - currentPoint.X));
                        cutList[idx].Add(y);
                    }

                    previousPoint = currentPoint;
                }
            }

            int idx2 = 0;
            for (long x = boundary.min.X + lineSpacing / 2; x < boundary.max.X; x += lineSpacing)
            {
                cutList[idx2].Sort();
                for (int i = 0; i + 1 < cutList[idx2].Count; i += 2)
                {
                    if (cutList[idx2][i + 1] - cutList[idx2][i] < extrusionWidth_um / 5)
                    {
                        continue;
                    }

                    Polygon p = new Polygon();
                    result.Add(p);
                    p.Add(matrix.unapply(new IntPoint(x, cutList[idx2][i])));
                    p.Add(matrix.unapply(new IntPoint(x, cutList[idx2][i + 1])));
                }

                idx2 += 1;
            }
        }

        public static void GenerateLineInfill(ConfigSettings config, SliceLayerPart part, Polygons fillPolygons, int extrusionWidth_um, int fillAngle)
        {
            if (config.infillPercent <= 0)
            {
                throw new Exception("infillPercent must be gerater than 0.");
            }

            int linespacing_um = (int)(config.extrusionWidth_um / (config.infillPercent / 100));
            GenerateLinePaths(part.sparseOutline, fillPolygons, extrusionWidth_um, linespacing_um, config.infillExtendIntoPerimeter_um, fillAngle);
        }

        public static void GenerateGridInfill(ConfigSettings config, SliceLayerPart part, Polygons fillPolygons, int extrusionWidth_um, int fillAngle)
        {
            if (config.infillPercent <= 0)
            {
                throw new Exception("infillPercent must be gerater than 0.");
            }

            int linespacing_um = (int)(config.extrusionWidth_um / (config.infillPercent / 100) * 2);

            Infill.GenerateLinePaths(part.sparseOutline, fillPolygons, config.extrusionWidth_um, linespacing_um, config.infillExtendIntoPerimeter_um, fillAngle);
            
            int fillAngle90 = fillAngle + 90;
            if (fillAngle90 > 360)
            {
                fillAngle90 -= 360;
            }

            Infill.GenerateLinePaths(part.sparseOutline, fillPolygons, extrusionWidth_um, linespacing_um, config.infillExtendIntoPerimeter_um, fillAngle90);
        }

        public static void generateConcentricInfill(Polygons outline, Polygons result, int inset_value, int inset_count)
        {
            for(int step = 0; step < inset_count; step++)
            {
                if (outline.Count < 1)
                {
                    break;
                }

                for (int polyNr = 0; polyNr < outline.Count; polyNr++)
                {
                    Polygon r = outline[polyNr];
                    result.Add(r);
                }
                outline = outline.Offset(-inset_value);
            }
        }
    }
}