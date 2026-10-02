using System.Globalization;
using System.Xml;

namespace AgesOfCalradia.WorldEventsShellRepair
{
    // UI-only transform of the guarded, cloned WorldEventsFrame. Never applied
    // to other prefabs or saved XML. Native map-marker and symbolic glyph sizes
    // stay intact; text-heavy detail boxes scroll rather than shrink to fit.
    internal static class WorldEventsReadability
    {
        internal static void Apply(XmlElement frame)
        {
            int index = 0;
            foreach (XmlElement text in frame.SelectNodes(".//TextWidget"))
            {
                string content = text.GetAttribute("Text");
                if (content == "◆" || content == "@Glyph" || content == "P" || content == "C" || content == "A") continue;
                float size;
                if (float.TryParse(text.GetAttribute("Brush.FontSize"), NumberStyles.Float, CultureInfo.InvariantCulture, out size) && size < 16f)
                    text.SetAttribute("Brush.FontSize", "16");
                float height;
                bool fixedHeight = text.GetAttribute("HeightSizePolicy") == "Fixed"
                    && float.TryParse(text.GetAttribute("SuggestedHeight"), NumberStyles.Float, CultureInfo.InvariantCulture, out height);
                // Reserve a readable line box; do not enlarge table containers.
                if (fixedHeight && float.Parse(text.GetAttribute("SuggestedHeight"), CultureInfo.InvariantCulture) < 20f)
                    text.SetAttribute("SuggestedHeight", "20");
                if (fixedHeight && (content == "@NotesText" || content == "@SummaryText" || content == "@BackgroundText"))
                    MakeScrollable(text, ++index);
            }
        }

        private static void MakeScrollable(XmlElement text, int index)
        {
            XmlDocument doc = text.OwnerDocument;
            string prefix = "WorldEventsReadableDetail" + index.ToString(CultureInfo.InvariantCulture);
            string bodyId = text.HasAttribute("Id") ? text.GetAttribute("Id") : prefix + "Body";
            XmlElement scroll = doc.CreateElement("ScrollablePanel");
            // Copy placement/visibility only; text bindings and brush stay on body.
            foreach (string attribute in new[] { "WidthSizePolicy", "HeightSizePolicy", "SuggestedWidth", "SuggestedHeight",
                "HorizontalAlignment", "VerticalAlignment", "MarginLeft", "MarginRight", "MarginTop", "MarginBottom",
                "PositionXOffset", "PositionYOffset", "IsVisible", "DataSource" })
                if (text.HasAttribute(attribute)) scroll.SetAttribute(attribute, text.GetAttribute(attribute));
            scroll.SetAttribute("Id", prefix);
            scroll.SetAttribute("InnerPanel", prefix + "Clip\\" + bodyId);
            scroll.SetAttribute("ClipRect", prefix + "Clip");
            scroll.SetAttribute("MouseScrollAxis", "Vertical");
            XmlElement children = doc.CreateElement("Children");
            XmlElement clip = doc.CreateElement("Widget");
            clip.SetAttribute("Id", prefix + "Clip");
            clip.SetAttribute("WidthSizePolicy", "StretchToParent");
            clip.SetAttribute("HeightSizePolicy", "StretchToParent");
            clip.SetAttribute("ClipContents", "true");
            XmlElement clipChildren = doc.CreateElement("Children");
            XmlElement body = (XmlElement)text.CloneNode(true);
            body.SetAttribute("Id", bodyId);
            foreach (string attribute in new[] { "SuggestedHeight", "SuggestedWidth", "MarginLeft", "MarginRight", "MarginTop", "MarginBottom",
                "PositionXOffset", "PositionYOffset", "IsVisible", "DataSource" }) body.RemoveAttribute(attribute);
            body.SetAttribute("WidthSizePolicy", "StretchToParent");
            body.SetAttribute("HeightSizePolicy", "CoverChildren");
            body.SetAttribute("HorizontalAlignment", "Left");
            body.SetAttribute("VerticalAlignment", "Top");
            body.SetAttribute("Brush.TextHorizontalAlignment", "Left");
            body.SetAttribute("Brush.TextVerticalAlignment", "Top");
            clipChildren.AppendChild(body);
            clip.AppendChild(clipChildren);
            children.AppendChild(clip);
            scroll.AppendChild(children);
            text.ParentNode.ReplaceChild(scroll, text);
        }
    }
}
