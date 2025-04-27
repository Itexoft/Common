// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Linq;
using Itexoft.Formats.Configuration.Ini;

namespace Itexoft.Tests.Formats.Configuration.Ini;

public sealed class IniParserTests
{
    [Test]
    public void ParsesSectionsAndKeyValues()
    {
        var text =
            "; comment\r\n" +
            "[Options]\r\n" +
            "Auto Index=Yes\r\n" +
            "Title=Hello World\r\n" +
            "List=one, two, \"three, four\"\r\n" +
            "\r\n" +
            "[Files]\r\n" +
            "file1.html\r\n" +
            "file2.html\r\n";

        var doc = IniDocument.Parse(text);

        Assert.That(doc.Sections.Count, Is.EqualTo(2));

        var options = doc.Sections["Options"]!;
        Assert.That(options.TryGetValue("Auto Index", out var autoIndex), Is.True);
        Assert.That(autoIndex.TryGetBoolean(out var autoValue), Is.True);
        Assert.That(autoValue, Is.True);
        Assert.That(options.GetValues("Title")[0].ToString(), Is.EqualTo("Hello World"));

        var list = options.GetValues("List");
        Assert.That(list.Count, Is.EqualTo(1));
        Assert.That(list[0].ToString(), Is.EqualTo("one, two, \"three, four\""));

        var files = doc.Sections["Files"]!;
        var fileNames = files.Values.Select(v => v.Value.ToString()).ToArray();
        Assert.That(fileNames, Is.EqualTo(new[] { "file1.html", "file2.html" }));
    }

    [Test]
    public void IgnoresInlineCommentsButKeepsQuotedSemicolons()
    {
        var text =
            "[Alias]\n" +
            "100=/path/file.html ; comment\n" +
            "Key=\"a; b\"\n" +
            "Other=/tmp/app # comment\n" +
            "Hash=#value\n";

        var doc = IniDocument.Parse(text);
        var section = doc.Sections["Alias"]!;

        Assert.That(section.GetValues("100")[0].ToString(), Is.EqualTo("/path/file.html"));
        Assert.That(section.GetValues("Key")[0].ToString(), Is.EqualTo("a; b"));
        Assert.That(section.GetValues("Other")[0].ToString(), Is.EqualTo("/tmp/app"));
        Assert.That(section.GetValues("Hash")[0].ToString(), Is.EqualTo("#value"));
    }

    [Test]
    public void ParsesGlobalEntries()
    {
        var text = "foo=bar\n[Sec]\nbaz=qux\n";
        var doc = IniDocument.Parse(text);

        Assert.That(doc.Global.TryGetValue("foo", out var value), Is.True);
        Assert.That(value.ToString(), Is.EqualTo("bar"));
    }

    [Test]
    public void CollectsDuplicateKeys()
    {
        var text = "[Sec]\nKey=one\nKey=two\n";
        var section = IniDocument.Parse(text).Sections["Sec"]!;

        var values = section.GetValues("Key");
        Assert.That(values.Count, Is.EqualTo(2));
        Assert.That(values[0].ToString(), Is.EqualTo("one"));
        Assert.That(values[1].ToString(), Is.EqualTo("two"));
    }

    [Test]
    public void ParsesNumbersAndBooleans()
    {
        var text = "[Sec]\nHex=0x1A\nDec=42\nFlag=No\n";
        var section = IniDocument.Parse(text).Sections["Sec"]!;

        Assert.That(section.GetValues("Hex")[0].TryGetNumber(out var hex), Is.True);
        Assert.That(hex, Is.EqualTo(26));

        Assert.That(section.GetValues("Dec")[0].TryGetInt32(out var dec), Is.True);
        Assert.That(dec, Is.EqualTo(42));

        Assert.That(section.GetValues("Flag")[0].TryGetBoolean(out var flag), Is.True);
        Assert.That(flag, Is.False);
    }

    [Test]
    public void SupportsEmptyValue()
    {
        var text = "[Sec]\nKey=\n";
        var section = IniDocument.Parse(text).Sections["Sec"]!;
        var values = section.GetValues("Key");

        Assert.That(values.Count, Is.EqualTo(1));
        Assert.That(values[0].IsEmpty, Is.True);
    }
}
