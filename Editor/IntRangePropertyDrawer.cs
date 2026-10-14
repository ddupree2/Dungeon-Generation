using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace StarStoneStudio.DungeonGeneration.Editor
{
    /// <summary>
    /// Draws an <see cref="IntRange"/> on a single inspector line as "Min [ ] Max [ ]".
    /// </summary>
    [CustomPropertyDrawer(typeof(IntRange))]
    internal class IntRangePropertyDrawer : PropertyDrawer
    {
        private const int BoundLabelWidth = 30;

        public override VisualElement CreatePropertyGUI(SerializedProperty serializedProperty)
        {
            // Mimic a BaseField so the label lines up with the rest of the inspector
            var rangeField = new VisualElement { tooltip = serializedProperty.tooltip };
            rangeField.AddToClassList(BaseField<int>.ussClassName);
            rangeField.AddToClassList(BaseField<int>.alignedFieldUssClassName);

            var rangeLabel = new Label(serializedProperty.displayName);
            rangeLabel.AddToClassList(BaseField<int>.labelUssClassName);

            var boundsContainer = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    flexGrow = 1
                }
            };

            boundsContainer.Add(BoundField("Min", serializedProperty.FindPropertyRelative("minimum")));
            boundsContainer.Add(BoundField("Max", serializedProperty.FindPropertyRelative("maximum")));

            rangeField.Add(rangeLabel);
            rangeField.Add(boundsContainer);

            return rangeField;
        }

        /// <summary>
        /// Creates a compact integer field bound to one end of the range.
        /// </summary>
        private static IntegerField BoundField(string boundLabel, SerializedProperty boundProperty)
        {
            var boundField = new IntegerField(boundLabel)
            {
                style =
                {
                    flexGrow = 1,
                    flexBasis = 0
                }
            };

            boundField.labelElement.style.minWidth = BoundLabelWidth;
            boundField.labelElement.style.width = BoundLabelWidth;
            boundField.BindProperty(boundProperty);

            return boundField;
        }
    }
}
