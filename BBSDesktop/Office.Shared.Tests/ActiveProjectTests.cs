// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using Xunit;

namespace Office.Shared.Tests;

public class ActiveProjectTests
{
    const string Good = "{\"format\":\"studio-active-project\",\"version\":1,\"app\":\"O-Draft\",\"drawing\":\"C:\\\\p\\\\house.dwg\",\"project\":\"C:\\\\p\\\\house.osp\",\"updated_utc\":\"2026-10-29T09:00:00Z\"}";

    [Fact]
    public void A_pointer_is_read()
    {
        var a = ActiveProject.Parse(Good)!;
        Assert.Equal("O-Draft", a.App);
        Assert.Equal(@"C:\p\house.dwg", a.Drawing);
        Assert.Equal(@"C:\p\house.osp", a.Project);
        Assert.Equal(new DateTime(2026, 10, 29, 9, 0, 0, DateTimeKind.Utc), a.UpdatedUtc);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{\"format\":\"other\",\"version\":1,\"project\":\"C:\\\\a.osp\"}")]
    [InlineData("{\"format\":\"studio-active-project\",\"version\":1,\"project\":null}")]            // O-Draft closed its drawing
    [InlineData("{\"format\":\"studio-active-project\",\"version\":1,\"project\":\"a.vdb\"}")]         // not an absolute path
    [InlineData("{\"format\":\"studio-active-project\",\"version\":1,\"project\":\"C:\\\\a.exe\"}")]   // not a project file
    [InlineData("{\"format\":\"studio-active-project\",\"project\":\"C:\\\\a.osp\"}")]                // no version
    public void Anything_else_gives_no_project(string json) => Assert.Null(ActiveProject.Parse(json));

    [Fact]
    public void Paths_are_compared_without_regard_to_case() =>
        Assert.True(ActiveProject.SamePath(@"C:\P\House.osp", @"c:\p\house.osp"));
}
