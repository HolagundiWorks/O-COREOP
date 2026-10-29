// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using Xunit;

namespace Office.Shared.Tests;

public class ActiveProjectTests
{
    const string Good = "{\"format\":\"osas-active-project\",\"version\":1,\"app\":\"O-Draft\",\"drawing\":\"C:\\\\p\\\\house.dwg\",\"shilpidb\":\"C:\\\\p\\\\house.dwg.takeoff.vdb\",\"updated_utc\":\"2026-10-29T09:00:00Z\"}";

    [Fact]
    public void A_pointer_is_read()
    {
        var a = ActiveProject.Parse(Good)!;
        Assert.Equal("O-Draft", a.App);
        Assert.Equal(@"C:\p\house.dwg", a.Drawing);
        Assert.Equal(@"C:\p\house.dwg.takeoff.vdb", a.ShilpiDb);
        Assert.Equal(new DateTime(2026, 10, 29, 9, 0, 0, DateTimeKind.Utc), a.UpdatedUtc);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{\"format\":\"other\",\"version\":1,\"shilpidb\":\"C:\\\\a.vdb\"}")]
    [InlineData("{\"format\":\"osas-active-project\",\"version\":1,\"shilpidb\":null}")]            // O-Draft closed its drawing
    [InlineData("{\"format\":\"osas-active-project\",\"version\":1,\"shilpidb\":\"a.vdb\"}")]         // not an absolute path
    [InlineData("{\"format\":\"osas-active-project\",\"version\":1,\"shilpidb\":\"C:\\\\a.exe\"}")]   // not a ShilpiDB file
    [InlineData("{\"format\":\"osas-active-project\",\"shilpidb\":\"C:\\\\a.vdb\"}")]                // no version
    public void Anything_else_gives_no_project(string json) => Assert.Null(ActiveProject.Parse(json));

    [Fact]
    public void Paths_are_compared_without_regard_to_case() =>
        Assert.True(ActiveProject.SamePath(@"C:\P\House.vdb", @"c:\p\house.vdb"));
}
