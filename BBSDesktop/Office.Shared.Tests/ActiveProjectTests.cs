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

    [Theory]
    [InlineData("http://127.0.0.1:7421", "http://127.0.0.1:7421")]
    [InlineData("http://localhost:7421/", "http://localhost:7421")]
    [InlineData("http://192.168.1.5:7421", null)]       // a pointer must not send the project to another computer
    [InlineData("https://127.0.0.1:7421", null)]
    [InlineData("not a url", null)]
    public void The_server_must_be_on_this_computer(string url, string? expected) => Assert.Equal(expected, ActiveProject.LoopbackUrl(url));

    [Fact]
    public void A_pointer_with_a_server_carries_it_and_one_without_has_none()
    {
        var with = ActiveProject.Parse(Good.Replace("\"updated_utc\"", "\"server\":\"http://127.0.0.1:7421\",\"updated_utc\""))!;
        Assert.Equal("http://127.0.0.1:7421", with.Server);
        Assert.Null(ActiveProject.Parse(Good)!.Server);
    }
}
