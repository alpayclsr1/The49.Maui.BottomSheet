using System.Diagnostics;
using System.Reflection.Metadata;
using Android.Content;
using Android.Content.Res;
using Android.Graphics.Drawables;
using Android.Hardware.Lights;
using Android.OS;
using Android.Runtime;
using Android.Util;
using Android.Views;
using Android.Widget;
using AndroidX.AppCompat.App;
using AndroidX.Core.View;
using Google.Android.Material.BottomSheet;
using Google.Android.Material.Color;
using Microsoft.Maui.Platform;
using AView = Android.Views.View;
using Debug = System.Diagnostics.Debug;

namespace The49.Maui.BottomSheet;

public class BottomSheetController
{
    private readonly IMauiContext _mauiContext;
    private readonly BottomSheet _bottomSheet;
    private readonly BottomSheetDialog _bottomSheetDialog;
    private BottomSheetDragHandleView? _handle;
    private readonly BottomSheetCallback _bottomSheetCallback;
    private readonly IDialogInterfaceOnDismissListener _dismissListener;
    private FrameLayout _bottomSheetContainer;
    bool? _isBackgroundLight;
    internal IDictionary<Detent, int> _states;
    internal IDictionary<Detent, double> _heights;

    public BottomSheetController(IMauiContext mauiContext, BottomSheet sheet)
    {
        _mauiContext = mauiContext;
        _bottomSheet = sheet;

        _bottomSheetDialog = new BottomSheetDialog(_mauiContext.Context);
        _bottomSheetDialog.ShowEvent += OnShow;
        _bottomSheetDialog.DismissEvent += OnDismissed;


        _bottomSheetCallback = new BottomSheetCallback();
        _bottomSheetCallback.StateChanged += BottomSheetCallbackOnStateChanged;
    }

    private int GetMaxHeight()
    {
        return _mauiContext.Context.Resources?.DisplayMetrics?.HeightPixels ?? 0;
    }

    private void OnShow(object? sender, EventArgs e)
    {
        _bottomSheet.NotifyShown();
    }

    private void OnDismissed(object? sender, EventArgs e)
    {
        _bottomSheet.NotifyDismissed();
        _bottomSheetDialog.Behavior.RemoveBottomSheetCallback(_bottomSheetCallback);
    }

    private void BottomSheetCallbackOnStateChanged(object? sender, BottomSheetStateChangedEventArgs e)
    {
        Debug.WriteLine($"Callback state changed: {e.State}");

        if (e.State == BottomSheetBehavior.StateHidden)
        {
            Dispose();
            _bottomSheet.NotifyDismissed();
        }

        UpdateSelectedDetent();
    }

    internal void CalculateHeights(double maxSheetHeight)
    {
        var detents = _bottomSheet.GetEnabledDetents().ToList();
        var density = DeviceDisplay.MainDisplayInfo.Density;
        var screenHeightPx = _mauiContext.Context.Resources.DisplayMetrics.HeightPixels;
        var screenHeightDp = screenHeightPx / density;

        _heights = new Dictionary<Detent, double>();

        foreach (var detent in detents)
        {
            double height;

            if (detent is ContentDetent)
            {
                height = CalculateContentHeight(); // içeriğin doğal yüksekliği
            }
            else if (detent is RatioDetent ratioDetent)
            {
                // ekran yüksekliğinin yüzdesi
                height = screenHeightDp * ratioDetent.Ratio;
            }
            else if (detent is FullscreenDetent)
            {
                // tam ekran yüksekliği
                height = screenHeightDp;
            }
            else
            {
                // özel/custom detent
                height = detent.GetHeight(_bottomSheet, maxSheetHeight);
            }

            _heights[detent] = height;

            Console.WriteLine($"📏 Detent: {detent.GetType().Name}, Calculated Height: {height}");
        }
    }

    internal void CalculateStates()
    {
        var heights = _heights.OrderByDescending(kv => kv.Value).ToList();
        _states = new Dictionary<Detent, int>();

        if (heights.Count == 0)
            return;

        if (heights.Count == 1)
        {
            _states[heights[0].Key] = BottomSheetBehavior.StateCollapsed;
        }
        else if (heights.Count == 2)
        {
            _states[heights[0].Key] = BottomSheetBehavior.StateExpanded;
            _states[heights[1].Key] = BottomSheetBehavior.StateCollapsed;
        }
        else
        {
            _states[heights[0].Key] = BottomSheetBehavior.StateExpanded;
            _states[heights[heights.Count / 2].Key] = BottomSheetBehavior.StateHalfExpanded;
            _states[heights[^1].Key] = BottomSheetBehavior.StateCollapsed;
        }

        foreach (var s in _states)
            Debug.WriteLine($"[StateMap] {s.Key.GetType().Name} => {s.Value}");
    }

    internal int GetStateForDetent(Detent detent)
    {
        if (detent is null || !_states.ContainsKey(detent))
        {
            Debug.WriteLine($"Detent not found in states: {detent}");
            return -1;
        }
        return _states[detent];
    }

    internal Detent GetDetentForState(int state)
    {
        var detent = _states.FirstOrDefault(kv => kv.Value == state).Key;
        Console.WriteLine($"State: {state}, Detent: {detent}");
        return detent;
    }

    internal void UpdateSelectedDetent()
    {
        var detent = GetDetentForState(_bottomSheetDialog.Behavior.State);
        if (detent is not null)
        {
            _bottomSheet.SelectedDetent = detent;
            Console.WriteLine($"SelectedDetent updated to: {detent}");
        }
    }

    internal void UpdateStateFromDetent()
    {
        if (_bottomSheet.SelectedDetent is null)
        {
            Console.WriteLine("SelectedDetent is null, setting to default.");
            _bottomSheet.SelectedDetent = _bottomSheet.GetDefaultDetent();
        }

        if (_bottomSheetDialog.Behavior is null || _states is null)
        {
            Console.WriteLine("Behavior or States is null in UpdateStateFromDetent.");
            return;
        }

        var state = GetStateForDetent(_bottomSheet.SelectedDetent);
        if (state != -1)
        {
            _bottomSheetDialog.Behavior.State = state;
            Console.WriteLine($"State updated from detent: {state}");
        }
    }

    internal void LayoutDetents(IDictionary<Detent, double> heights, double maxSheetHeight)
    {
        if (heights == null || !heights.Any()) return;

        var sorted = heights.OrderByDescending(kv => kv.Value).ToList();
        var density = DeviceDisplay.MainDisplayInfo.Density;
        var behavior = _bottomSheetDialog.Behavior;

        var context = _mauiContext.Context;
        var displayMetrics = context.Resources.DisplayMetrics;
        var fullHeight = displayMetrics.HeightPixels;
        var fullWidth = displayMetrics.WidthPixels;

        behavior.FitToContents = false;
        behavior.SkipCollapsed = false;

        var topHeight = sorted[0].Value * density;
        var topDetent = sorted[0].Key;

        var selected = _bottomSheet.SelectedDetent;

        if (selected is RatioDetent ratioDetent)
        {
            behavior.FitToContents = false;
            behavior.SkipCollapsed = false;

            var fullHeightPx = displayMetrics.HeightPixels;

            var windowInsets = ViewCompat.GetRootWindowInsets(_bottomSheetContainer);
            AndroidX.Core.Graphics.Insets insets = windowInsets != null
                ? windowInsets.GetInsets(WindowInsetsCompat.Type.SystemBars())
                : AndroidX.Core.Graphics.Insets.Of(0, 0, 0, 0);

            var usableHeightPx = fullHeightPx - insets.Top - insets.Bottom;
            var usableHeightDp = usableHeightPx / density;

            // 🎯 Android 15 için yükseklik oranını %10 azalt
            var ratio = ratioDetent.Ratio;
            if (Build.VERSION.SdkInt == BuildVersionCodes.VanillaIceCream) // Android 15
            {
                ratio *= (float)0.9; // %10 azalt
                Debug.WriteLine("📉 Android 15 detected — Ratio reduced by 10%");
            }

            var targetHeightDp = usableHeightDp * ratio;
            var targetHeightPx = (int)(targetHeightDp * density);

            behavior.ExpandedOffset = fullHeightPx - targetHeightPx;
            behavior.PeekHeight = targetHeightPx;
            behavior.State = BottomSheetBehavior.StateExpanded;

            var bottomSheetView = _bottomSheetDialog.FindViewById(Resource.Id.design_bottom_sheet);
            if (bottomSheetView != null)
            {
                var layoutParams = bottomSheetView.LayoutParameters;
                layoutParams.Height = targetHeightPx;
                bottomSheetView.LayoutParameters = layoutParams;
            }

            if (_bottomSheet.GetEnabledDetents().Count() == 1)
            {
                behavior.Draggable = false;
                Debug.WriteLine("🛑 Only one detent — Draggable disabled.");
            }

            Debug.WriteLine($"🧩 RatioDetent applied: Ratio={ratioDetent.Ratio}, FinalRatio={ratio}, Insets Top: {insets.Top}, Bottom: {insets.Bottom}");
            return;
        }

        if (selected is FullscreenDetent fullscreenDetent)
        {
            behavior.FitToContents = false;
            behavior.SkipCollapsed = false;

            var fullHeightPx = displayMetrics.HeightPixels;

            var windowInsets = ViewCompat.GetRootWindowInsets(_bottomSheetContainer);
            AndroidX.Core.Graphics.Insets insets = windowInsets != null
                ? windowInsets.GetInsets(WindowInsetsCompat.Type.SystemBars())
                : AndroidX.Core.Graphics.Insets.Of(0, 0, 0, 0);

            var usableHeightPx = fullHeightPx - insets.Top - insets.Bottom;
            var usableHeightDp = usableHeightPx / density;
            float ratio = 1;
            if (Build.VERSION.SdkInt == BuildVersionCodes.VanillaIceCream) // Android 15
            {
                ratio *= (float)0.9; // %10 azalt
                Debug.WriteLine("📉 Android 15 detected — Ratio reduced by 10%");
            }

            var targetHeightDp = usableHeightDp * ratio;
            var targetHeightPx = (int)(targetHeightDp * density);

            behavior.ExpandedOffset = fullHeightPx - targetHeightPx;
            behavior.PeekHeight = targetHeightPx;
            behavior.State = BottomSheetBehavior.StateExpanded;

            var bottomSheetView = _bottomSheetDialog.FindViewById(Resource.Id.design_bottom_sheet);
            if (bottomSheetView != null)
            {
                var layoutParams = bottomSheetView.LayoutParameters;
                layoutParams.Height = targetHeightPx;
                bottomSheetView.LayoutParameters = layoutParams;
            }

            if (_bottomSheet.GetEnabledDetents().Count() == 1)
            {
                behavior.Draggable = false;
                Debug.WriteLine("🛑 Only one detent — Draggable disabled.");
            }
            return;
        }

        // Half detent
        if (sorted.Count >= 3)
        {
            var midHeight = sorted[1].Value * density;
            var containerHeight = _bottomSheetContainer?.Height ?? fullHeight;
            behavior.HalfExpandedRatio = (float)Math.Clamp(midHeight / containerHeight, 0.1, 0.9);
        }

        // Collapsed detent
        var collapsedDetent = _states.FirstOrDefault(x => x.Value == BottomSheetBehavior.StateCollapsed).Key;
        if (collapsedDetent != null && heights.TryGetValue(collapsedDetent, out var collapsedHeight))
        {
            behavior.PeekHeight = (int)(collapsedHeight * density);
        }

        Debug.WriteLine($"LayoutDetents: ExpandedOffset={behavior.ExpandedOffset}, PeekHeight={behavior.PeekHeight}, HalfRatio={behavior.HalfExpandedRatio}");
    }



 private double CalculateContentHeight()
{
    var density = DeviceDisplay.MainDisplayInfo.Density;
    var contentView = _bottomSheet.ToPlatform(_mauiContext);

    var displayMetrics = _mauiContext.Context.Resources.DisplayMetrics;

    // Genişlik sınırlı, yükseklik serbest
    int widthMeasureSpec = Android.Views.View.MeasureSpec.MakeMeasureSpec(displayMetrics.WidthPixels, MeasureSpecMode.AtMost);
    int heightMeasureSpec = Android.Views.View.MeasureSpec.MakeMeasureSpec(0, MeasureSpecMode.Unspecified);

    contentView.Measure(widthMeasureSpec, heightMeasureSpec);

    var measuredHeight = contentView.MeasuredHeight / density;
    Console.WriteLine($"📐 Corrected content height: {measuredHeight}");
    return measuredHeight;
}

  


    public void Show(bool animated)
    {
        var detents = _bottomSheet.GetEnabledDetents().ToList();

        if (detents.Any(d => d is ContentDetent))
        {
            _bottomSheet.SelectedDetent = detents.First(d => d is ContentDetent);
        }
        var inflater = LayoutInflater.From(_mauiContext.Context);
        var rootView = inflater.Inflate(Resource.Layout.the49_maui_bottom_sheet_design, null);

        var containerView = _bottomSheet.ToPlatform(_mauiContext);
        _bottomSheetContainer = rootView.FindViewById<FrameLayout>(Resource.Id.design_bottom_sheet);
        _bottomSheetContainer.RemoveAllViews();

        if (_bottomSheet.HasHandle)
            AddHandle();

        _bottomSheetContainer.AddView(containerView);
        _bottomSheetDialog.SetContentView(rootView);

        var maxHeight = GetAvailableHeight();
        Debug.WriteLine($"Max height from GetAvailableHeight: {maxHeight}");

        double contentHeight = CalculateContentHeight();
        _bottomSheetContainer.SetMinimumHeight((int)(contentHeight * DeviceDisplay.MainDisplayInfo.Density));

        // 🧮 Detent yüksekliklerini hesapla
        CalculateHeights(maxHeight);
        CalculateStates();

        // ✅ En kısa detent ile başla (örneğin ContentDetent)
        var shortest = _heights.OrderBy(kv => kv.Value).FirstOrDefault().Key;
        _bottomSheet.SelectedDetent = shortest ?? _bottomSheet.GetDefaultDetent();

        // 📐 MaxHeight ayarla
        _bottomSheetDialog.Behavior.MaxHeight = (int)(maxHeight * DeviceDisplay.MainDisplayInfo.Density);

        // 🛠️ Davranışları uygula
        LayoutDetents(_heights, maxHeight);

        var state = GetStateForDetent(_bottomSheet.SelectedDetent);
        if (state == -1)
        {
            state = BottomSheetBehavior.StateCollapsed;
            Debug.WriteLine("Fallback state applied.");
        }

        _bottomSheetDialog.Behavior.State = state;
        _bottomSheetDialog.Behavior.FitToContents = true; // çoklu detent için
        // _bottomSheetDialog.Behavior.SkipCollapsed = false;
        _bottomSheetDialog.Behavior.Hideable = _bottomSheet.IsCancelable;


        _bottomSheetDialog.Behavior.Hideable = _bottomSheet.IsCancelable;
        _bottomSheetDialog.Behavior.FitToContents = true;
        _bottomSheetDialog.Behavior.State = BottomSheetBehavior.StateCollapsed;


        _bottomSheetDialog.Window.DecorView.SetPadding(0, 0, 0, 0);

        if (_heights.Count > 1)
        {
            _bottomSheetDialog.Behavior.Draggable = true;
        }
        else
        {
            _bottomSheetDialog.Behavior.Draggable = false;
        }

        _bottomSheetDialog.Window?.SetSoftInputMode(SoftInput.AdjustResize);


        UpdateBackground();
        UpdateHasBackdrop();

        if (!animated)
            _bottomSheetDialog.Window.SetWindowAnimations(0);

        _bottomSheetDialog.Behavior.AddBottomSheetCallback(_bottomSheetCallback);
        _bottomSheetDialog.SetCancelable(_bottomSheet.IsCancelable);

        _bottomSheet.NotifyShowing();

        if (_bottomSheet.SelectedDetent is ContentDetent)
        {
            HookSizeChange(_bottomSheet.ToPlatform(_mauiContext));
        }

        _bottomSheetDialog.SetOnKeyListener(new BackKeyListener(() =>
        {

            // Geri tuşunu MAUI'ye ilet
            var currentPage = Shell.Current.CurrentPage;

            if (currentPage != null)
            {
                // Bu, Page.OnBackButtonPressed()’i tetikler
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    var result = currentPage.SendBackButtonPressed(); // async değil, bool döner
                    Debug.WriteLine($"➡️ Page.OnBackButtonPressed triggered: {result}");
                });
            }

            return true; // geri tuşunu biz yönettik
        }));


        _bottomSheetDialog.Show();



    }



    public void Dismiss(bool animated)
    {
        if (_bottomSheetDialog is null || !_bottomSheetDialog.IsShowing)
        {
            return;
        }

        if (animated)
        {
            _bottomSheetDialog.Dismiss();
        }
        else
        {
            _bottomSheetDialog.Cancel();
        }
    }

    private double GetAvailableHeight()
    {
        var windowManager = _mauiContext.Context.GetSystemService(Context.WindowService) as IWindowManager;
        if (windowManager != null)
        {
            var metrics = new DisplayMetrics();
            windowManager.DefaultDisplay.GetMetrics(metrics);

            var density = DeviceDisplay.MainDisplayInfo.Density;
            var screenHeight = metrics.HeightPixels / density;
            Debug.WriteLine($"Screen height: {screenHeight}");
            /*
            if (_bottomSheet.SelectedDetent is ContentDetent)
            {
                var contentHeight = CalculateContentHeight();
                Debug.WriteLine($"Returning content height for ContentDetent: {contentHeight}");
                return contentHeight;
            }
            */
            return screenHeight;
        }

        Debug.WriteLine("WindowManager not available, returning default height: 600");
        return 600;
    }
    /*
        private void AddHandle()
        {
            var density = DeviceDisplay.MainDisplayInfo.Density;

            _handle = new BottomSheetDragHandleView(_mauiContext.Context);
            _handle.SetColorFilter(Android.Graphics.Color.Gray); // Görünür renk

            // ✅ Yüksekliği sabitle
            var layoutParams = new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                (int)(24 * density) // 24dp yükseklik
            );

            // ✅ Yukarı hizala (en üstte yer alsın)
            layoutParams.Gravity = GravityFlags.Top;

            _bottomSheetContainer.AddView(_handle, layoutParams);
        }

    */



    private void AddHandle()
    {
        var density = DeviceDisplay.MainDisplayInfo.Density;

        var handleView = new BottomSheetDragHandleView(_mauiContext.Context);
        var layoutParams = new FrameLayout.LayoutParams(
             (int)(36 * density),
            (int)(4 * density) // 4dp yükseklik
        );
        layoutParams.Gravity = GravityFlags.Top | GravityFlags.CenterHorizontal;
        layoutParams.SetMargins(0, (int)(8 * density), 0, 0);
        handleView.LayoutParameters = layoutParams;
        handleView.SetBackgroundColor(Android.Graphics.Color.Gray);
        _bottomSheetContainer.AddView(handleView, 0);
    }

    public void UpdateHandleColor()
    {
        if (_handle is null)
        {
            return;
        }
        if (_bottomSheet.HandleColor is not null)
        {
            _handle.SetColorFilter(_bottomSheet.HandleColor.ToPlatform());
        }
    }

    void Dispose()
    {
        if (_bottomSheetDialog is not null)
        {
            _bottomSheetDialog.Behavior.RemoveBottomSheetCallback(_bottomSheetCallback);
        }
    }

    public void UpdateBackground()
    {
        if (_bottomSheet is null || _bottomSheetContainer is null)
        {
            return;
        }

        Paint paint = _bottomSheet.BackgroundBrush;
        if (_bottomSheetContainer is not null)
        {
            if (_bottomSheet.CornerRadius != -1)
            {
                SheetRadiusDrawable drawable;
                if (_bottomSheetContainer.Background is not SheetRadiusDrawable)
                {
                    drawable = new SheetRadiusDrawable();
                    _bottomSheetContainer.Background = drawable;
                }
                else
                {
                    drawable = (SheetRadiusDrawable)_bottomSheetContainer.Background;
                }
                drawable.SetCornerRadius(_bottomSheetContainer.Context.ToPixels(_bottomSheet.CornerRadius));
            }
            if (paint is not null)
            {
                var platformColor = paint.ToColor().ToPlatform();
                if (_bottomSheetContainer.Background is SheetRadiusDrawable sheetDrawable)
                {
                    sheetDrawable.SetColor(platformColor);
                }
                else
                {
                    _bottomSheetContainer.BackgroundTintList = ColorStateList.ValueOf(platformColor);
                }
            }
        }
        ColorStateList backgroundTint = ViewCompat.GetBackgroundTintList(_bottomSheetContainer);

        if (backgroundTint != null)
        {
            _isBackgroundLight = MaterialColors.IsColorLight(backgroundTint.DefaultColor);
        }
        else if (_bottomSheetContainer.Background is ColorDrawable)
        {
            _isBackgroundLight = MaterialColors.IsColorLight(((ColorDrawable)_bottomSheetContainer.Background).Color);
        }
        else
        {
            _isBackgroundLight = null;
        }
    }
    private int _lastMeasuredHeight = -1;


    private void HookSizeChange(AView view)
    {
        if (view == null) return;

        var observer = view.ViewTreeObserver;
        if (!observer.IsAlive)
            return;

        observer.GlobalLayout += (sender, args) =>
        {
            if (_bottomSheet.SelectedDetent is ContentDetent)
            {
                // Mevcut yükseklikle karşılaştır, değişmişse güncelle
                var newHeight = view.MeasuredHeight;
                if (Math.Abs(newHeight - _lastMeasuredHeight) > 5) // 5px fark varsa güncelle
                {
                    Debug.WriteLine($"🔄 Detected height change: {_lastMeasuredHeight} → {newHeight}");

                    _lastMeasuredHeight = newHeight;

                    var maxHeight = GetAvailableHeight();
                    CalculateHeights(maxHeight);
                    CalculateStates();
                    LayoutDetents(_heights, maxHeight);
                }
            }
        };
    }

    public void UpdateHasBackdrop()
    {
        if (_bottomSheet is null || _bottomSheetDialog is null || _bottomSheetDialog.Window is null)
        {
            return;
        }

        var window = _bottomSheetDialog.Window;

        if (_bottomSheet.HasBackdrop)
        {
            window.AddFlags(WindowManagerFlags.DimBehind);
        }
        else
        {
            window.ClearFlags(WindowManagerFlags.DimBehind);
        }
    }
}
public class BackKeyListener : Java.Lang.Object, IDialogInterfaceOnKeyListener
{
    private readonly Func<bool> _onBackPressed;

    public BackKeyListener(Func<bool> onBackPressed)
    {
        _onBackPressed = onBackPressed;
    }

    public bool OnKey(IDialogInterface dialog, [GeneratedEnum] Keycode keyCode, KeyEvent e)
    {
        if (keyCode == Keycode.Back && e.Action == KeyEventActions.Up)
        {
            return _onBackPressed.Invoke();
        }
        return false;
    }
}


