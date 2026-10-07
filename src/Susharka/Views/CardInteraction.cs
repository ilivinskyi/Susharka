using System;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Susharka.Native;

namespace Susharka.Views;

/// <summary>
/// Gestures on a photo: click copies, double-click opens, press-and-hold marks up, dragging hands the
/// file to another app or folder, right-click shows the menu.
/// </summary>
internal sealed class CardInteraction
{
    /// <summary>Long enough not to fire on a slow click, short enough to feel responsive.</summary>
    private const double LongPressSeconds = 0.45;
    private const double DragThreshold = 4;

    private readonly PeggedCard _card;
    private readonly LinePanel _panel;
    private readonly DispatcherTimer _holdTimer;
    private Point _downAt;
    private bool _pressed, _longPressed, _dragging, _suppressClick;

    private CardInteraction(PeggedCard card, LinePanel panel)
    {
        _card = card;
        _panel = panel;
        _holdTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(LongPressSeconds) };
        _holdTimer.Tick += (_, _) => OnLongPress();

        var target = card.Frame;
        target.MouseLeftButtonDown += OnDown;
        target.MouseMove += OnMove;
        target.MouseLeftButtonUp += OnUp;
        target.LostMouseCapture += (_, _) => { if (_pressed && !_dragging) Cancel(); };
        target.MouseRightButtonUp += OnRightClick;
    }

    public static void Attach(PeggedCard card, LinePanel panel) => _ = new CardInteraction(card, panel);

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (e.ClickCount >= 2)
        {
            // Double-click: open. The first click already copied, which is harmless.
            Cancel();
            _suppressClick = true;
            _panel.Actions?.Open(_card.Item);
            return;
        }
        _pressed = true;
        _longPressed = false;
        _suppressClick = false;
        _downAt = e.GetPosition(_panel);
        _card.Frame.CaptureMouse();
        _card.SetPressed(true);
        _panel.BeginBusy();
        _holdTimer.Start();
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        // OLE keeps delivering mouse moves during DoDragDrop; never start a nested drag.
        if (!_pressed || _longPressed || _dragging || e.LeftButton != MouseButtonState.Pressed) return;
        var p = e.GetPosition(_panel);
        if (Math.Abs(p.X - _downAt.X) < DragThreshold && Math.Abs(p.Y - _downAt.Y) < DragThreshold) return;
        StartDrag();
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (!_pressed)
        {
            return;
        }
        var wasLong = _longPressed;
        Cancel();
        if (!wasLong && !_suppressClick)
        {
            _card.ShowCopied();
            _panel.Actions?.Copy(_card.Item);
        }
    }

    private void OnLongPress()
    {
        _holdTimer.Stop();
        if (!_pressed || _dragging) return;
        _longPressed = true;
        _card.SetPressed(false);
        _panel.Actions?.Markup(_card.Item);
    }

    private void Cancel()
    {
        _holdTimer.Stop();
        if (_pressed) _panel.EndBusy();
        _pressed = false;
        if (_card.Frame.IsMouseCaptured) _card.Frame.ReleaseMouseCapture();
        _card.SetPressed(false);
    }

    private void StartDrag()
    {
        _holdTimer.Stop();
        _dragging = true;
        if (_card.Frame.IsMouseCaptured) _card.Frame.ReleaseMouseCapture();
        _card.SetPressed(false);
        _card.SetDragging(true);

        var data = new DataObject();
        data.SetFileDropList(new StringCollection { _card.Item.Path });

        DragGhost? ghost = null;
        if (_card.Thumbnail is { } thumb)
        {
            ghost = new DragGhost(thumb, _card.FrameW - 8, _card.FrameH - 8, _card.Item.Tilt);
            ghost.Show();
            ghost.Follow();
        }

        void Feedback(object s, GiveFeedbackEventArgs a) => ghost?.Follow();
        void Query(object s, QueryContinueDragEventArgs a) => ghost?.Follow();
        _card.Frame.GiveFeedback += Feedback;
        _card.Frame.QueryContinueDrag += Query;
        try
        {
            // Copy into apps, move into folders (Explorer chooses), the Recycle Bin deletes.
            DragDrop.DoDragDrop(_card.Frame, data, DragDropEffects.Copy | DragDropEffects.Move);
        }
        catch (Exception ex) { AppController.Log("Drag failed: " + ex.Message); }
        finally
        {
            _card.Frame.GiveFeedback -= Feedback;
            _card.Frame.QueryContinueDrag -= Query;
            ghost?.Close();
            _card.SetDragging(false);
            _dragging = false;
            Cancel();
            _panel.Actions?.DragEnded(_card.Item);
        }
    }

    private void OnRightClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        var actions = _panel.Actions;
        if (actions == null) return;
        var item = _card.Item;

        var menu = new ContextMenu();
        void Add(string header, Action act, string? gesture = null)
        {
            var mi = new MenuItem { Header = header, InputGestureText = gesture ?? "" };
            mi.Click += (_, _) => act();
            menu.Items.Add(mi);
        }
        Add("Copy", () => { _card.ShowCopied(); actions.Copy(item); }, "Click");
        Add("Open", () => actions.Open(item), "Double-click");
        Add("Mark up", () => actions.Markup(item), "Hold");
        menu.Items.Add(new Separator());
        Add("Show in Explorer", () => actions.ShowInExplorer(item));
        Add("Save as…", () => actions.SaveAs(item));
        menu.Items.Add(new Separator());
        Add("Let go", () => actions.LetGo(item));
        Add("Delete", () => actions.Delete(item));

        _panel.MenuOpened();
        menu.Closed += (_, _) => _panel.MenuClosed();
        menu.PlacementTarget = _card.Frame;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        _panel.BringToFront();
        menu.IsOpen = true;
    }
}
