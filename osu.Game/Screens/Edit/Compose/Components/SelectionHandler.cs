﻿// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using Humanizer;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.States;
using osu.Game.Audio;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Rulesets.Edit;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Objects.Types;
using osuTK;

namespace osu.Game.Screens.Edit.Compose.Components
{
    /// <summary>
    /// A component which outlines <see cref="DrawableHitObject"/>s and handles movement of selections.
    /// </summary>
    public class SelectionHandler : CompositeDrawable, IKeyBindingHandler<PlatformAction>, IHasContextMenu
    {
        public const float BORDER_RADIUS = 2;

        public IEnumerable<SelectionBlueprint> SelectedBlueprints => selectedBlueprints;
        private readonly List<SelectionBlueprint> selectedBlueprints;

        public int SelectedCount => selectedBlueprints.Count;

        public IEnumerable<HitObject> SelectedHitObjects => selectedBlueprints.Select(b => b.HitObject);

        private Drawable content;

        private OsuSpriteText selectionDetailsText;

        [Resolved(CanBeNull = true)]
        protected EditorBeatmap EditorBeatmap { get; private set; }

        [Resolved(CanBeNull = true)]
        protected IEditorChangeHandler ChangeHandler { get; private set; }

        public SelectionHandler()
        {
            selectedBlueprints = new List<SelectionBlueprint>();

            RelativeSizeAxes = Axes.Both;
            AlwaysPresent = true;
            Alpha = 0;
        }

        [BackgroundDependencyLoader]
        private void load(OsuColour colours)
        {
            createStateBindables();

            InternalChild = content = new Container
            {
                Children = new Drawable[]
                {
                    new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        Masking = true,
                        BorderThickness = BORDER_RADIUS,
                        BorderColour = colours.YellowDark,
                        Child = new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            AlwaysPresent = true,
                            Alpha = 0
                        }
                    },
                    new Container
                    {
                        Name = "info text",
                        AutoSizeAxes = Axes.Both,
                        Children = new Drawable[]
                        {
                            new Box
                            {
                                Colour = colours.YellowDark,
                                RelativeSizeAxes = Axes.Both,
                            },
                            selectionDetailsText = new OsuSpriteText
                            {
                                Padding = new MarginPadding(2),
                                Colour = colours.Gray0,
                                Font = OsuFont.Default.With(size: 11)
                            }
                        }
                    }
                }
            };
        }

        #region User Input Handling

        /// <summary>
        /// Handles the selected <see cref="DrawableHitObject"/>s being moved.
        /// </summary>
        /// <remarks>
        /// Just returning true is enough to allow <see cref="HitObject.StartTime"/> updates to take place.
        /// Custom implementation is only required if other attributes are to be considered, like changing columns.
        /// </remarks>
        /// <param name="moveEvent">The move event.</param>
        /// <returns>
        /// Whether any <see cref="DrawableHitObject"/>s could be moved.
        /// Returning true will also propagate StartTime changes provided by the closest <see cref="IPositionSnapProvider.SnapScreenSpacePositionToValidTime"/>.
        /// </returns>
        public virtual bool HandleMovement(MoveSelectionEvent moveEvent) => true;

        public bool OnPressed(PlatformAction action)
        {
            switch (action.ActionMethod)
            {
                case PlatformActionMethod.Delete:
                    deleteSelected();
                    return true;
            }

            return false;
        }

        public void OnReleased(PlatformAction action)
        {
        }

        #endregion

        #region Selection Handling

        /// <summary>
        /// Bind an action to deselect all selected blueprints.
        /// </summary>
        internal Action DeselectAll { private get; set; }

        /// <summary>
        /// Handle a blueprint becoming selected.
        /// </summary>
        /// <param name="blueprint">The blueprint.</param>
        internal void HandleSelected(SelectionBlueprint blueprint)
        {
            selectedBlueprints.Add(blueprint);

            // there are potentially multiple SelectionHandlers active, but we only want to add hitobjects to the selected list once.
            if (!EditorBeatmap.SelectedHitObjects.Contains(blueprint.HitObject))
                EditorBeatmap.SelectedHitObjects.Add(blueprint.HitObject);

            UpdateVisibility();
        }

        /// <summary>
        /// Handle a blueprint becoming deselected.
        /// </summary>
        /// <param name="blueprint">The blueprint.</param>
        internal void HandleDeselected(SelectionBlueprint blueprint)
        {
            selectedBlueprints.Remove(blueprint);

            EditorBeatmap.SelectedHitObjects.Remove(blueprint.HitObject);

            UpdateVisibility();
        }

        /// <summary>
        /// Handle a blueprint requesting selection.
        /// </summary>
        /// <param name="blueprint">The blueprint.</param>
        /// <param name="state">The input state at the point of selection.</param>
        internal void HandleSelectionRequested(SelectionBlueprint blueprint, InputState state)
        {
            if (state.Keyboard.ControlPressed)
            {
                if (blueprint.IsSelected)
                    blueprint.Deselect();
                else
                    blueprint.Select();
            }
            else
            {
                if (blueprint.IsSelected)
                    return;

                DeselectAll?.Invoke();
                blueprint.Select();
            }
        }

        private void deleteSelected()
        {
            ChangeHandler?.BeginChange();

            foreach (var h in selectedBlueprints.ToList())
                EditorBeatmap?.Remove(h.HitObject);

            ChangeHandler?.EndChange();
        }

        #endregion

        #region Outline Display

        /// <summary>
        /// Updates whether this <see cref="SelectionHandler"/> is visible.
        /// </summary>
        internal void UpdateVisibility()
        {
            int count = selectedBlueprints.Count;

            selectionDetailsText.Text = count > 0 ? count.ToString() : string.Empty;

            if (count > 0)
                Show();
            else
                Hide();
        }

        protected override void Update()
        {
            base.Update();

            if (selectedBlueprints.Count == 0)
                return;

            // Move the rectangle to cover the hitobjects
            var topLeft = new Vector2(float.MaxValue, float.MaxValue);
            var bottomRight = new Vector2(float.MinValue, float.MinValue);

            foreach (var blueprint in selectedBlueprints)
            {
                topLeft = Vector2.ComponentMin(topLeft, ToLocalSpace(blueprint.SelectionQuad.TopLeft));
                bottomRight = Vector2.ComponentMax(bottomRight, ToLocalSpace(blueprint.SelectionQuad.BottomRight));
            }

            topLeft -= new Vector2(5);
            bottomRight += new Vector2(5);

            content.Size = bottomRight - topLeft;
            content.Position = topLeft;
        }

        #endregion

        #region Sample Changes

        /// <summary>
        /// Adds a hit sample to all selected <see cref="HitObject"/>s.
        /// </summary>
        /// <param name="sampleName">The name of the hit sample.</param>
        public void AddHitSample(string sampleName)
        {
            ChangeHandler?.BeginChange();

            foreach (var h in SelectedHitObjects)
            {
                // Make sure there isn't already an existing sample
                if (h.Samples.Any(s => s.Name == sampleName))
                    continue;

                h.Samples.Add(new HitSampleInfo { Name = sampleName });
            }

            ChangeHandler?.EndChange();
        }

        /// <summary>
        /// Set the new combo state of all selected <see cref="HitObject"/>s.
        /// </summary>
        /// <param name="state">Whether to set or unset.</param>
        /// <exception cref="InvalidOperationException">Throws if any selected object doesn't implement <see cref="IHasComboInformation"/></exception>
        public void SetNewCombo(bool state)
        {
            ChangeHandler?.BeginChange();

            foreach (var h in SelectedHitObjects)
            {
                var comboInfo = h as IHasComboInformation;

                if (comboInfo == null || comboInfo.NewCombo == state) continue;

                comboInfo.NewCombo = state;
                EditorBeatmap?.UpdateHitObject(h);
            }

            ChangeHandler?.EndChange();
        }

        /// <summary>
        /// Removes a hit sample from all selected <see cref="HitObject"/>s.
        /// </summary>
        /// <param name="sampleName">The name of the hit sample.</param>
        public void RemoveHitSample(string sampleName)
        {
            ChangeHandler?.BeginChange();

            foreach (var h in SelectedHitObjects)
                h.SamplesBindable.RemoveAll(s => s.Name == sampleName);

            ChangeHandler?.EndChange();
        }

        #endregion

        #region Selection State

        /// <summary>
        /// The state of "new combo" for all selected hitobjects.
        /// </summary>
        public readonly Bindable<TernaryState> SelectionNewComboState = new Bindable<TernaryState>();

        /// <summary>
        /// The state of each sample type for all selected hitobjects. Keys match with <see cref="HitSampleInfo"/> constant specifications.
        /// </summary>
        public readonly Dictionary<string, Bindable<TernaryState>> SelectionSampleStates = new Dictionary<string, Bindable<TernaryState>>();

        /// <summary>
        /// Set up ternary state bindables and bind them to selection/hitobject changes (in both directions)
        /// </summary>
        private void createStateBindables()
        {
            foreach (var sampleName in HitSampleInfo.AllAdditions)
            {
                var bindable = new Bindable<TernaryState>
                {
                    Description = sampleName.Replace("hit", string.Empty).Titleize()
                };

                bindable.ValueChanged += state =>
                {
                    switch (state.NewValue)
                    {
                        case TernaryState.False:
                            RemoveHitSample(sampleName);
                            break;

                        case TernaryState.True:
                            AddHitSample(sampleName);
                            break;
                    }
                };

                SelectionSampleStates[sampleName] = bindable;
            }

            // new combo
            SelectionNewComboState.ValueChanged += state =>
            {
                switch (state.NewValue)
                {
                    case TernaryState.False:
                        SetNewCombo(false);
                        break;

                    case TernaryState.True:
                        SetNewCombo(true);
                        break;
                }
            };

            // bring in updates from selection changes
            EditorBeatmap.HitObjectUpdated += _ => UpdateTernaryStates();
            EditorBeatmap.SelectedHitObjects.CollectionChanged += (sender, args) => UpdateTernaryStates();
        }

        /// <summary>
        /// Called when context menu ternary states may need to be recalculated (selection changed or hitobject updated).
        /// </summary>
        protected virtual void UpdateTernaryStates()
        {
            SelectionNewComboState.Value = GetStateFromSelection(SelectedHitObjects.OfType<IHasComboInformation>(), h => h.NewCombo);

            foreach (var (sampleName, bindable) in SelectionSampleStates)
            {
                bindable.Value = GetStateFromSelection(SelectedHitObjects, h => h.Samples.Any(s => s.Name == sampleName));
            }
        }

        /// <summary>
        /// Given a selection target and a function of truth, retrieve the correct ternary state for display.
        /// </summary>
        protected TernaryState GetStateFromSelection<T>(IEnumerable<T> selection, Func<T, bool> func)
        {
            if (selection.Any(func))
                return selection.All(func) ? TernaryState.True : TernaryState.Indeterminate;

            return TernaryState.False;
        }

        #endregion

        #region Context Menu

        public MenuItem[] ContextMenuItems
        {
            get
            {
                if (!selectedBlueprints.Any(b => b.IsHovered))
                    return Array.Empty<MenuItem>();

                var items = new List<MenuItem>();

                items.AddRange(GetContextMenuItemsForSelection(selectedBlueprints));

                if (selectedBlueprints.All(b => b.HitObject is IHasComboInformation))
                {
                    items.Add(new TernaryStateMenuItem("New combo") { State = { BindTarget = SelectionNewComboState } });
                }

                if (selectedBlueprints.Count == 1)
                    items.AddRange(selectedBlueprints[0].ContextMenuItems);

                items.AddRange(new[]
                {
                    new OsuMenuItem("Sound")
                    {
                        Items = SelectionSampleStates.Select(kvp =>
                            new TernaryStateMenuItem(kvp.Value.Description) { State = { BindTarget = kvp.Value } }).ToArray()
                    },
                    new OsuMenuItem("Delete", MenuItemType.Destructive, deleteSelected),
                });

                return items.ToArray();
            }
        }

        /// <summary>
        /// Provide context menu items relevant to current selection. Calling base is not required.
        /// </summary>
        /// <param name="selection">The current selection.</param>
        /// <returns>The relevant menu items.</returns>
        protected virtual IEnumerable<MenuItem> GetContextMenuItemsForSelection(IEnumerable<SelectionBlueprint> selection)
            => Enumerable.Empty<MenuItem>();

        #endregion
    }
}
