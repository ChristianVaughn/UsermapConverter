﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Globalization;
using System.Threading.Tasks;
using static UsermapConverter.Usermap;
using UsermapConverter.Windows;

namespace UsermapConverter
{
    class UsermapConversion
    {

        public static Home myHome = null;
        
        public static void addStatus(string text)
        {
            myHome.Dispatcher.Invoke(() =>
            {
                myHome.UpdateTestwhateverText(text);
            });
        }

        public static void ConvertUsermap(string srcFileName, string destFileName)
        {
            var tagMap = LoadTagMap();


            UsermapConversion.addStatus("Opening " + srcFileName);

            using (var srcStream = new EndianStream(File.Open(srcFileName, FileMode.Open, FileAccess.ReadWrite), EndianStream.EndianType.LittleEndian))
            using (var outStream = new EndianStream(File.Open(destFileName, FileMode.Create, FileAccess.ReadWrite), EndianStream.EndianType.LittleEndian))
            {
                SandboxMap srcMap = Usermap.DeserializeSandboxMap(srcStream);

                if (!File.Exists(GetCanvasFileName(srcMap.MapId)))
                    return;

                using (var canvasStream = new EndianStream(File.OpenRead(GetCanvasFileName(srcMap.MapId)), EndianStream.EndianType.LittleEndian))
                {
                    var canvasMap = Usermap.DeserializeSandboxMap(canvasStream);

                    var newBudgetEntries = new List<BudgetEntry>();
                    var newPlacements = new List<SandboxPlacement>();

                    for (int i = 0; i < 640; i++)
                        newPlacements.Add(canvasMap.Placements[i].Clone());


                    UsermapConversion.addStatus("Source map has " + srcMap.ScnrObjectCount + " scenario objects");

                    for (int i = 0; i < srcMap.ScnrObjectCount; i++) // This first loop, I believe, is for so-called "scenario objects"
                    {
                        var srcPlacement = srcMap.Placements[i];

                        if (srcPlacement.BudgetIndex == -1)
                            continue;

                        var tagIndex = srcMap.Budget[srcPlacement.BudgetIndex].TagIndex;
                        var tagIndexDiscarded = false;

                        uint? newTagIndex;
                        if(tagMap.TryGetValue(tagIndex, out newTagIndex)) // If the map contains this tagIndex, it means there was a change. If not, nevermind this, just move along.
                        {

                            if (newTagIndex.HasValue) // If new tag index isn't empty, it has to be mapped to a new value
                            {
                                UsermapConversion.addStatus(tagIndex.ToString("X") + " mapped to " + ((uint)newTagIndex).ToString("X"));
                                tagIndex = (uint)newTagIndex;
                            }
                            else // Else it simply doesn't exist anymore and has to be discarded
                            {
                                UsermapConversion.addStatus(tagIndex.ToString("X") + " discarded");
                                tagIndexDiscarded = true;
                            }
                        }

                        if (tagIndexDiscarded)
                            continue;
                        

                        var newBudgetIndex = canvasMap.Budget.FindIndex(e => e.TagIndex == tagIndex);

                        var substitutePlacements = canvasMap.Placements
                            .Where((x, j) =>
                            (int)(newPlacements[j].PlacementFlags & (1 << 5)) != 0 && // deleted
                            x.BudgetIndex == newBudgetIndex).ToList();

                        var newPlacementIndex2 = canvasMap.Placements.IndexOf(substitutePlacements.FirstOrDefault());
                        if (newPlacementIndex2 == -1)
                            continue;

                        newPlacements[newPlacementIndex2] = srcPlacement.Clone();
                        newPlacements[newPlacementIndex2].PlacementFlags |= (1 << 1); //touched
                    }

                    var newPlacementIndex = canvasMap.ScnrObjectCount;
                    short? emptyPlacementIndex = null; // If a placement index is unused, I can use it for the map options

                    for (var i = srcMap.ScnrObjectCount; i < 640; i++, newPlacementIndex++) // This one is for the normal placements I believe
                    {
                        var srcPlacement = srcMap.Placements[i];

                        if (srcPlacement.BudgetIndex == -1)
                        {
                            if (!emptyPlacementIndex.HasValue) { emptyPlacementIndex = newPlacementIndex; UsermapConversion.addStatus("Unused placement index " + emptyPlacementIndex + " will be used for map options"); }
                            continue;
                        }

                        var tagIndex = srcMap.Budget[srcPlacement.BudgetIndex].TagIndex;
                        var tagIndexDiscarded = false;

                        uint? newTagIndex;
                        if (tagMap.TryGetValue(tagIndex, out newTagIndex)) // If the map contains this tagIndex, it means there was a change. If not, nevermind this, just move along.
                        {

                            if (newTagIndex.HasValue) // If new tag index isn't empty, it has to be mapped to a new value
                            {
                                UsermapConversion.addStatus(tagIndex.ToString("X") + " mapped to " + ((uint)newTagIndex).ToString("X"));
                                tagIndex = (uint)newTagIndex;
                            }
                            else // Else it simply doesn't exist anymore and has to be discarded
                            {
                                UsermapConversion.addStatus(tagIndex.ToString("X") + " discarded");
                                tagIndexDiscarded = true;
                            }
                        }


                        if (tagIndexDiscarded)
                        {
                            if (!emptyPlacementIndex.HasValue) { emptyPlacementIndex = newPlacementIndex; UsermapConversion.addStatus("Unused placement index " + emptyPlacementIndex + " will be used for map options"); }
                            continue;
                        }

                        var newBudgetIndex = canvasMap.Budget.FindIndex(e => e.TagIndex == tagIndex);
                        if (newBudgetIndex == -1)
                        {
                            var n = newBudgetEntries.FindIndex(e => e.TagIndex == tagIndex);

                            if (n == -1)
                            {
                                var entry = new BudgetEntry()
                                {
                                    TagIndex = tagIndex,
                                    Cost = -1,
                                    RuntimeMin = 0,
                                    RuntimeMax = 0,
                                    CountOnMap = 0,
                                    DesignTimeMax = 0
                                };

                                newBudgetIndex = canvasMap.BudgetEntryCount + newBudgetEntries.Count();
                                newBudgetEntries.Add(entry);


                                UsermapConversion.addStatus("injecting 0x" + tagIndex.ToString("X") + " "+ newBudgetIndex);
                                Console.WriteLine("injecting 0x{0} {1}", tagIndex.ToString("X"), newBudgetIndex);
                            }
                            else
                            {
                                newBudgetIndex = canvasMap.BudgetEntryCount + n;
                            }
                        }


                        var newPlacement = newPlacements[newPlacementIndex] = srcPlacement.Clone();
                        newPlacement.BudgetIndex = newBudgetIndex;
                    }


                    // Copy over map options (hex tag index 5728) from the canvas map (necessary because of the map barriers, may be leading to crashes in out of bound maps)
                    var mapOptionsTagIndex = uint.Parse("5728", NumberStyles.HexNumber);
                    for (var i = 0; i < 640; i++) // This one is for the normal placements I believe
                    {
                        var canvasPlacement = canvasMap.Placements[i];
                        if (canvasPlacement.BudgetIndex != -1)
                        {

                            var tagIndex = canvasMap.Budget[canvasPlacement.BudgetIndex].TagIndex;
                            if(tagIndex == mapOptionsTagIndex)
                            {
                                if (emptyPlacementIndex.HasValue)
                                {
                                    UsermapConversion.addStatus("Placing map options (0x5728) with disabled barriers in placement index "+emptyPlacementIndex);
                                    newPlacements[(int)emptyPlacementIndex] = canvasPlacement.Clone();
                                } else
                                {
                                    // There is no space to place map options. Sucks. Will either have to overwrite one element or ditch them.
                                }
                            }
                        }
                    }

                    canvasStream.SeekTo(0);
                    canvasStream.Stream.CopyTo(outStream.Stream);

                    outStream.SeekTo(0x30);
                    srcStream.SeekTo(0x30);
                    outStream.WriteBytes(srcStream.ReadBytes(0x108)); //chdr
                    outStream.WriteBytes(srcStream.ReadBytes(0x140)); //map

                    outStream.SeekTo(0x242);
                    outStream.WriteInt16(canvasMap.ScnrObjectCount);
                    outStream.WriteInt16((short)(canvasMap.ScnrObjectCount + (srcMap.TotalObjectCount - srcMap.ScnrObjectCount)));
                    outStream.WriteInt16((short)(canvasMap.BudgetEntryCount + newBudgetEntries.Count + 1));

                    outStream.SeekTo(0x278);
                    foreach (var placement in newPlacements)
                        Usermap.SerializePlacement(outStream, placement);

                    outStream.SeekTo(0xD498 + canvasMap.BudgetEntryCount * 0xc);
                    foreach (var entry in newBudgetEntries)
                        Usermap.SerializeBudgetEntry(outStream, entry);
                }
            }
        }

        public static Dictionary<uint, uint?> LoadTagMap()
        {
            var tagMap = Properties.Resources.map;
            var result = new Dictionary<uint, uint?>();
            using (var reader = new StringReader(tagMap))
            {
                while (true)
                {
                    var line = reader.ReadLine();
                    if (line == null)
                        break;
                    line = line.Trim();
                    var parts = line.Split(',');
                    if (parts.Length < 2)
                        continue;
                    uint oldIndex;
                    uint newIndexTmp;
                    uint? newIndex;
                    if (!uint.TryParse(parts[0].Trim(), NumberStyles.HexNumber, null, out oldIndex))
                        continue;
                    if (!uint.TryParse(parts[1].Trim(), NumberStyles.HexNumber, null, out newIndexTmp))
                    {
                        newIndex = null;
                    } else
                    {
                        newIndex = newIndexTmp;
                    }
                    result[oldIndex] = newIndex;
                }
            }
            return result;
        }

        public static string GetCanvasFileName(int mapId)
        {
            return Path.Combine(@"canvas", string.Format("{0}_sandbox.map", mapId));
        }
    }
}
