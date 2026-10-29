// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using System.Text;
using Xunit;

namespace Office.Shared.Tests;

public class ShilpiProjectFileTests
{
    static VdbRecord Takeoff(ulong id, string json) => new(id, 1, 2, 3, 4, Encoding.UTF8.GetBytes(json));

    [Fact]
    public void A_file_round_trips_with_its_records_and_version()
    {
        var c = new VdbContents { Version = 2 };
        c.Records.Add(Takeoff((1UL << 56) | 5, "{\"a\":1}"));
        var back = ShilpiProjectFile.Parse(ShilpiProjectFile.Serialize(c));
        Assert.Equal(2u, back.Version);
        var r = Assert.Single(back.Records);
        Assert.Equal((1UL << 56) | 5, r.Id);
        Assert.Equal(new[] { 1.0, 2.0, 3.0, 4.0 }, new[] { r.MinX, r.MinY, r.MaxX, r.MaxY });
        Assert.Equal("{\"a\":1}", Encoding.UTF8.GetString(r.Payload));
    }

    [Fact]
    public void The_project_is_stored_and_read_back_and_other_records_are_kept()
    {
        var c = new VdbContents();
        c.Records.Add(Takeoff((3UL << 56) | 1, "{\"ref\":\"W1\"}"));
        ShilpiProjectFile.SetProject(c, "{\"project\":{\"name\":\"House — Ä\"}}");
        var back = ShilpiProjectFile.Parse(ShilpiProjectFile.Serialize(c));
        Assert.Equal("{\"project\":{\"name\":\"House — Ä\"}}", ShilpiProjectFile.GetProject(back));
        Assert.Contains(back.Records, r => r.Kind == 3);
    }

    [Fact]
    public void Saving_again_replaces_the_project_and_does_not_grow_the_file()
    {
        var c = new VdbContents();
        ShilpiProjectFile.SetProject(c, "{\"v\":1}");
        ShilpiProjectFile.SetProject(c, "{\"v\":2}");
        Assert.Single(c.Records);
        Assert.Equal("{\"v\":2}", ShilpiProjectFile.GetProject(c));
    }

    [Fact]
    public void A_large_project_is_split_in_chunks_and_joined_in_order()
    {
        var json = "{\"x\":\"" + new string('é', ShilpiProjectFile.ChunkBytes) + "\"}";   // two bytes a character: well over one chunk
        var c = new VdbContents();
        ShilpiProjectFile.SetProject(c, json);
        Assert.True(c.Records.Count >= 2);
        c.Records.Reverse();                                                              // order in the file must not matter
        Assert.Equal(json, ShilpiProjectFile.GetProject(c));
    }

    [Fact]
    public void A_take_off_file_without_a_project_gives_none() =>
        Assert.Null(ShilpiProjectFile.GetProject(new VdbContents { Records = { Takeoff((1UL << 56), "{}") } }));

    [Fact]
    public void SaveProject_keeps_what_another_program_wrote_since_the_project_was_opened()
    {
        var path = Path.Combine(Path.GetTempPath(), $"coreop-{Guid.NewGuid():N}.vdb");
        try
        {
            ShilpiProjectFile.SaveProject(path, "{\"v\":1}");
            // O-Draft records a wall into the same file after the project was opened
            var other = ShilpiProjectFile.Load(path);
            other.Records.Add(Takeoff((3UL << 56) | 9, "{\"ref\":\"W9\"}"));
            File.WriteAllBytes(path, ShilpiProjectFile.Serialize(other));
            ShilpiProjectFile.SaveProject(path, "{\"v\":2}");
            var now = ShilpiProjectFile.Load(path);
            Assert.Equal("{\"v\":2}", ShilpiProjectFile.GetProject(now));
            Assert.Contains(now.Records, r => r.Id == ((3UL << 56) | 9));
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, Path.GetFileName(path) + ".tmp-*"));
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("not a vdb file at all")]
    [InlineData("SHILPIDB")]
    public void Garbage_is_refused(string text) =>
        Assert.Throws<InvalidDataException>(() => ShilpiProjectFile.Parse(Encoding.ASCII.GetBytes(text)));

    [Fact]
    public void A_file_cut_inside_a_record_is_refused()
    {
        var c = new VdbContents();
        ShilpiProjectFile.SetProject(c, "{\"v\":1}");
        var bytes = ShilpiProjectFile.Serialize(c);
        Assert.Throws<InvalidDataException>(() => ShilpiProjectFile.Parse(bytes[..^3]));
    }
}
