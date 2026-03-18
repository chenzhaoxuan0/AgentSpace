using System;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using AgentSpace.Core.Models;

namespace AgentSpace.Core.Services
{
    public class UIAutomationService
    {
        public UIAutomationService()
        {
        }

        public string ExtractTextFromRegion(IntPtr hwnd, NativeRect screenCropRegion)
        {
            try
            {
                var rootElement = AutomationElement.FromHandle(hwnd);
                if (rootElement == null) return string.Empty;

                var resultBuilder = new StringBuilder();
                
                // Convert NativeRect to System.Windows.Rect for easier intersection logic
                var cropRect = new Rect(screenCropRegion.Left, screenCropRegion.Top, 
                                        screenCropRegion.Right - screenCropRegion.Left, 
                                        screenCropRegion.Bottom - screenCropRegion.Top);

                WalkTreeAccumulatingText(rootElement, cropRect, resultBuilder);

                return resultBuilder.ToString().Trim();
            }
            catch (Exception ex)
            {
                return $"Error extracting text: {ex.Message}";
            }
        }

        private void WalkTreeAccumulatingText(AutomationElement element, Rect cropRect, StringBuilder builder)
        {
            try
            {
                // Check if the current element's bounding box intersects with our crop region
                var elementRect = element.Current.BoundingRectangle;
                
                // If it's completely outside our cropped blue box, ignore it and its children
                if (!cropRect.IntersectsWith(elementRect) && !elementRect.IsEmpty)
                {
                    return;
                }

                // If it's a Text or Edit control, try to extract its value
                if (element.Current.ControlType == ControlType.Text || 
                    element.Current.ControlType == ControlType.Edit ||
                    element.Current.ControlType == ControlType.Document)
                {
                    string text = GetText(element);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        builder.AppendLine(text);
                    }
                }

                // Walk the children
                var walkers = TreeWalker.ControlViewWalker;
                var child = walkers.GetFirstChild(element);
                while (child != null)
                {
                    WalkTreeAccumulatingText(child, cropRect, builder);
                    child = walkers.GetNextSibling(child);
                }
            }
            catch (ElementNotAvailableException)
            {
                // UI changes rapidly; ignore stale elements
            }
        }

        private string GetText(AutomationElement element)
        {
            // Try ValuePattern (for edit boxes)
            if (element.TryGetCurrentPattern(ValuePattern.Pattern, out object valuePatternObj))
            {
                var valuePattern = (ValuePattern)valuePatternObj;
                return valuePattern.Current.Value;
            }

            // Try TextPattern (for static text/documents)
            if (element.TryGetCurrentPattern(TextPattern.Pattern, out object textPatternObj))
            {
                var textPattern = (TextPattern)textPatternObj;
                return textPattern.DocumentRange.GetText(-1);
            }

            // Fallback to Name property (for buttons, simple labels)
            return element.Current.Name;
        }
    }
}
