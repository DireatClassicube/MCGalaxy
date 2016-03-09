/*
    Copyright 2010 MCSharp team (Modified for use with MCZall/MCLawl/MCGalaxy)
    
    Dual-licensed under the    Educational Community License, Version 2.0 and
    the GNU General Public License, Version 3 (the "Licenses"); you may
    not use this file except in compliance with the Licenses. You may
    obtain a copy of the Licenses at
    
    http://www.opensource.org/licenses/ecl2.php
    http://www.gnu.org/licenses/gpl-3.0.html
    
    Unless required by applicable law or agreed to in writing,
    software distributed under the Licenses are distributed on an "AS IS"
    BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express
    or implied. See the Licenses for the specific language governing
    permissions and limitations under the Licenses.
 */
// Copyright 2009, 2010 Matvei Stefarov <me@matvei.org>
/*
This generator was developed by Neko_baron.

Ideas, concepts, and code were used from the following two sources:
1) Isaac McGarvey's 'perlin noise generator' code
2) http://www.lighthouse3d.com/opengl/terrain/index.php3?introduction

 */
using System;
namespace MCGalaxy {
    
    public sealed class RealisticMapGen {
        
        float[] terrain, overlay, overlay2;

        float treeDens;
        short treeDist;
        Random rand;
        ushort LiquidLevel;
        MapGenParams genParams;
        
        public bool GenerateMap(Level Lvl, string type, int seed = 0, bool useSeed = false) {
            DateTime startTime = DateTime.UtcNow;
            Server.s.Log("Attempting map gen");
            rand = useSeed ? new System.Random(seed) : new System.Random();
            if (!MapGenParams.Themes.TryGetValue(type, out genParams))
                genParams = new MapGenParams();
            
            try
            {
                terrain = new float[Lvl.Width * Lvl.Length];
                overlay = new float[Lvl.Width * Lvl.Length];
                if (genParams.GenTrees) overlay2 = new float[Lvl.Width * Lvl.Length];
                LiquidLevel = genParams.GetLiquidLevel(Lvl.Height);

                GenerateFault(terrain, Lvl, rand);
                FilterAverage(Lvl);
                Server.s.Log("Creating overlay");
                GeneratePerlinNoise(overlay, Lvl, rand);

                if (genParams.GenerateOverlay2) {
                    Server.s.Log("Planning trees");
                    GeneratePerlinNoise(overlay2, Lvl, rand);
                }

                Server.s.Log("Converting height map, and applying overlays");
                float rangeLo = genParams.RangeLow;
                float rangeHi = genParams.RangeHigh;
                treeDens = genParams.TreeDens;
                treeDist = genParams.TreeDist;

                //loops though evey X/Z coordinate
                for (int index = 0; index < terrain.Length; index++) {
                    ushort x = (ushort)(index % Lvl.Width);
                    ushort z = (ushort)(index / Lvl.Width);
                    ushort y;
                    if (genParams.FalloffEdges) {
                        float offset = NegateEdge(x, z, Lvl);
                        y = Evaluate(Lvl, Range(terrain[index], rangeLo - offset, rangeHi - offset));
                    } else {
                        y = Evaluate(Lvl, Range(terrain[index], rangeLo, rangeHi));
                    }
                    
                    if (!genParams.UseLavaLiquid)
                        GenNonLavaColumn(x, y, z, Lvl, index);
                    else
                        GenLavaColumn(x, y, z, Lvl, index);
                }
                Server.s.Log("Total time was " + (DateTime.UtcNow - startTime).TotalSeconds.ToString() + " seconds.");
            } catch (Exception e) {
                Server.ErrorLog(e);
                Server.s.Log("Gen Fail");
                return false;
            }
            return true;
        }

        void GenNonLavaColumn(ushort x, ushort y, ushort z, Level Lvl, int index) {
            if (y > LiquidLevel) {
                for (ushort yy = 0; y - yy >= 0; yy++) {
                    if (genParams.SimpleColumns) {
                        Lvl.SetTile(x, (ushort)(y - yy), z, Block.sand);
                    } else if (overlay[index] < 0.72f) {
                        if (genParams.IslandColumns) { //increase sand height for island
                            if (y > LiquidLevel + 2) {
                                if (yy == 0) Lvl.SetTile(x, (ushort)(y - yy), z, Block.grass);     //top layer
                                else if (yy < 3) Lvl.SetTile(x, (ushort)(y - yy), z, Block.dirt);  //next few
                                else Lvl.SetTile(x, (ushort)(y - yy), z, Block.rock);              //ten rock it
                            } else {
                                Lvl.SetTile(x, (ushort)(y - yy), z, Block.sand);                   //SAAAND extra for islands
                            }
                        } else {
                            if (yy == 0) Lvl.SetTile(x, (ushort)(y - yy), z, Block.grass);
                            else if (yy < 3) Lvl.SetTile(x, (ushort)(y - yy), z, Block.dirt);
                            else Lvl.SetTile(x, (ushort)(y - yy), z, Block.rock);
                        }
                    } else {
                        Lvl.SetTile(x, (ushort)(y - yy), z, Block.rock);
                    }
                }

                if (genParams.GenFlowers && overlay[index] < 0.25f) {
                    switch (rand.Next(12)) {
                        case 10:
                            Lvl.SetTile(x, (ushort)(y + 1), z, Block.redflower); break;
                        case 11:
                            Lvl.SetTile(x, (ushort)(y + 1), z, Block.yellowflower); break;
                        default:
                            break;
                    }
                }
                
                if (genParams.GenTrees && overlay[index] < 0.65f && overlay2[index] < treeDens) {
                    if (Lvl.GetTile(x, (ushort)(y + 1), z) == Block.air) {
                        if (Lvl.GetTile(x, y, z) == Block.grass || genParams.UseCactus) {
                            if (rand.Next(13) == 0 && !TreeGen.TreeCheck(Lvl, x, y, z, treeDist))
                                if (genParams.UseCactus)
                                    TreeGen.AddCactus(Lvl, x, (ushort)(y + 1), z, rand);
                                else
                                    TreeGen.AddTree(Lvl, x, (ushort)(y + 1), z, rand);
                        }
                    }
                }
            } else { //Must be on/under the water line then
                for (ushort yy = 0; LiquidLevel - yy >= 0; yy++) {
                    if (LiquidLevel - yy > y) {
                        Lvl.SetTile(x, (ushort)(LiquidLevel - yy), z, Block.water);    //better fill the water above me
                    } else if (LiquidLevel - yy > y - 3) {
                        byte block = overlay[index] < 0.75f ? Block.sand : Block.gravel; // sand on top
                        Lvl.SetTile(x, (ushort)(LiquidLevel - yy), z, block);
                    } else {
                        Lvl.SetTile(x, (ushort)(LiquidLevel - yy), z, Block.rock);
                    }
                }
            }
        }
        
        void GenLavaColumn(ushort x, ushort y, ushort z, Level Lvl, int index) {
            if (y > LiquidLevel) {
                for (ushort yy = 0; y - yy >= 0; yy++) {
                    if (y > (LiquidLevel - 1)) {
                        if (yy == 0) Lvl.SetTile(x, (ushort)(y - yy), z, Block.rock);      //top layer
                        else if (yy < 3) Lvl.SetTile(x, (ushort)(y - yy), z, Block.rock);
                        else if (yy < 2) Lvl.SetTile(x, (ushort)(y - yy), z, Block.lava);//next few
                        else Lvl.SetTile(x, (ushort)(y - yy), z, Block.obsidian);
                    } else {
                        Lvl.SetTile(x, (ushort)(y - yy), z, Block.lava);
                    }
                    
                    if (overlay[index] < 0.3f) {
                        switch (rand.Next(13)) {
                            case 9:
                            case 10:
                            case 11:
                            case 12:
                                Lvl.SetTile(x, (ushort)(y + 1), z, Block.lava); //change to lava when time
                                break;
                            default:
                                break;
                        }
                    }
                    Lvl.SetTile(x, (ushort)(y), z, (rand.Next(100) % 3 == 1 ? Block.darkgrey : Block.obsidian));
                }
            } else {
                for (ushort yy = 0; LiquidLevel - yy >= 0; yy++) {
                    if (LiquidLevel - yy > y - 1) {
                        Lvl.SetTile(x, (ushort)(LiquidLevel - yy), z, Block.lava); //better fill the water above me
                    } else if (LiquidLevel - yy > y - 3) {
                        if (overlay[index] < 0.9f) {
                            byte block = yy < y ? Block.lava : Block.rock;
                            Lvl.SetTile(x, (ushort)(y - yy), z, block);
                        } else {
                            Lvl.SetTile(x, (ushort)(LiquidLevel - yy), (ushort)(z - 5), Block.lava);  //killer lava
                        }
                    } else {
                        Lvl.SetTile(x, (ushort)(LiquidLevel - yy), z, Block.stone); //and just make the rest cobblestone
                    }
                }
            }
        }
        
        
        void GenerateFault(float[] array, Level Lvl, Random rand) {
            float baseHeight = genParams.StartHeight;
            float dispMax = genParams.DisplacementMax;
            float dispStep = genParams.DisplacementStep;

            for (int i = 0; i < array.Length; i++)
                array[i] = baseHeight;
            float disp = dispMax;
            
            ushort halfX = (ushort)(Lvl.Width / 2), halfZ = (ushort)(Lvl.Length / 2);
            float d = (float)Math.Sqrt(halfX * halfX + halfZ * halfZ);
            int numIterations = Lvl.Width + Lvl.Length;
            Server.s.Log("Iterations = " + numIterations);
            
            for (int iter = 0; iter < numIterations; iter++) {            
                float phi = (float)(rand.NextDouble() * 360);
                float cosPhi = (float)Math.Cos(phi);
                float sinPhi = (float)Math.Sin(phi);
                float c = ((float)rand.NextDouble()) * 2 * d - d;

                int index = 0;
                for (ushort z = 0; z < Lvl.Length; z++)
                    for (ushort x = 0; x < Lvl.Width; x++)
                {
                    bool pos = (z - halfZ) * cosPhi + (x - halfX) * sinPhi + c > 0;
                    float sum = array[index] + (pos ? disp : -disp);
                    if (sum > 1) sum = 1;
                    if (sum < 0) sum = 0;
                    array[index] = sum;
                    index++;
                }
                disp += dispStep;
                if (disp < -dispMax) 
                    disp = dispMax;
            }
        }

        void GeneratePerlinNoise(float[] array, Level Lvl, Random rand) {
            NoiseGen.GenerateNormalized(array, 0.7f, 8, Lvl.Width, Lvl.Length, rand.Next(), 64);
        }

        //converts the float into a ushort for map height
        ushort Evaluate(Level lvl, float height) {
            ushort y = (ushort)(height * lvl.Height);
            if (y < 0) return 0;
            if (y > lvl.Height - 1) return (ushort)(lvl.Height - 1);
            return y;
        }

        //applys the average filter
        void FilterAverage(Level Lvl) {
            Server.s.Log("Applying average filtering");
            float[] filtered = new float[terrain.Length];

            for (int index = 0; index < terrain.Length; index++) {
                ushort x = (ushort)(index % Lvl.Width);
                ushort y = (ushort)(index / Lvl.Width);
                filtered[index] = GetAverage9(x, y, Lvl);
            }

            for (int bb = 0; bb < terrain.Length; bb++)
                terrain[bb] = filtered[bb];
        }

        //Averages over 5 points
        float GetAverage5(ushort x, ushort y, Level Lvl) {
            int points = 0;
            float sum = GetPixel(ref points, x, y, Lvl);
            sum += GetPixel(ref points, (ushort)(x + 1), y, Lvl);
            sum += GetPixel(ref points, (ushort)(x - 1), y, Lvl);
            sum += GetPixel(ref points, x, (ushort)(y + 1), Lvl);
            sum += GetPixel(ref points, x, (ushort)(y - 1), Lvl);

            return sum / points;
        }
        //Averages over 9 points
        float GetAverage9(ushort x, ushort y, Level Lvl) {
            int points = 0;
            float sum = GetPixel(ref points, x, y, Lvl);
            sum += GetPixel(ref points, (ushort)(x + 1), y, Lvl);
            sum += GetPixel(ref points, (ushort)(x - 1), y, Lvl);
            sum += GetPixel(ref points, x, (ushort)(y + 1), Lvl);
            sum += GetPixel(ref points, x, (ushort)(y - 1), Lvl);

            sum += GetPixel(ref points, (ushort)(x + 1), (ushort)(y + 1), Lvl);
            sum += GetPixel(ref points, (ushort)(x - 1), (ushort)(y + 1), Lvl);
            sum += GetPixel(ref points, (ushort)(x + 1), (ushort)(y - 1), Lvl);
            sum += GetPixel(ref points, (ushort)(x - 1), (ushort)(y - 1), Lvl);

            return sum / points;
        }

        //returns the value of a x,y terrain coordinate
        float GetPixel(ref int points, ushort x, ushort y, Level Lvl) {
            if (x < 0 || x >= Lvl.Width || y < 0 || y >= Lvl.Length)
                return 0;
            points++;
            return terrain[x + y * Lvl.Width];
        }

        //converts the height into a range
        float Range(float input, float low, float high) {
            if (high <= low) return low;
            return low + (input * (high - low));
        }

        //Forces the edge of a map to slope lower for island map types
        float NegateEdge(ushort x, ushort z, Level Lvl) {
            float tempx = 0.0f, tempy = 0.0f;
            float temp;
            if (x != 0) { tempx = ((float)x / (float)Lvl.Width) * 0.5f; }
            if (z != 0) { tempy = ((float)z / (float)Lvl.Length) * 0.5f; }
            tempx = Math.Abs(tempx - 0.25f);
            tempy = Math.Abs(tempy - 0.25f);
            
            if (tempx > tempy)
                temp = tempx - 0.15f;
            else
                temp = tempy - 0.15f;
            return temp > 0 ? temp : 0;
        }
    }
}