using System;
using System.Threading.Tasks;

using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.ApplicationModel.Core;
using Windows.Graphics.Holographic;
using Windows.Perception.Spatial;
using Windows.UI.Core;
using Windows.Foundation;
using Windows.UI.ViewManagement;

namespace CP.Universal.Holographic
{
    /// <summary>
    /// The base class for holographic views
    /// </summary>
    public abstract class HolographicView : IFrameworkView, IDisposable
    {
        #region Properties
        private bool WindowVisible
        {
            get;
            set;
        }

        private bool WindowClosed
        {
            get;
            set;
        }

        protected HolographicSpace HolographicSpace
        {
            get;
            private set;
        }

        protected DeviceResources DeviceResources
        {
            get;
            set;
        }

        protected StepTimer Timer
        {
            get;
            set;
        }
        #endregion

        #region Creation
        protected HolographicView()
        {
        }
        #endregion

        #region Rendering
        private HolographicFrame CreateNextFrame()
        {
            // Before doing the timer update, there is some work to do per-frame
            // to maintain holographic rendering. First, we will get information
            // about the current frame.

            // The HolographicFrame has information that the app needs in order
            // to update and render the current frame. The app begins each new
            // frame by calling CreateNextFrame.
            HolographicFrame holographicFrame = this.HolographicSpace.CreateNextFrame();

            // Get a prediction of where holographic cameras will be when this frame
            // is presented.
            HolographicFramePrediction prediction = holographicFrame.CurrentPrediction;

            // Back buffers can change from frame to frame. Validate each buffer, and recreate
            // resource views and depth buffers as needed.
            this.DeviceResources.EnsureCameraResources(holographicFrame, prediction);

            this.Timer.Tick(() =>
            {
                UpdateScene(holographicFrame);
            });

            // The holographic frame will be used to get up-to-date view and projection matrices and
            // to present the swap chain.
            return holographicFrame;
        }

        /// <summary>
        /// Update scene objects
        /// </summary>
        /// <param name="holographicFrame">The holographic frame</param>
        /// <remarks>
        /// Put time-based updates here. By default this code will run once per frame,
        /// but if you change the StepTimer to use a fixed time step this code will
        /// run as many times as needed to get to the current step.
        /// </remarks>
        protected virtual void UpdateScene(HolographicFrame holographicFrame)
        {
        }

        /// <summary>
        /// Renders the current frame to each holographic display, according to the 
        /// current application and spatial positioning state. Returns true if the 
        /// frame was rendered to at least one display.
        /// </summary>
        protected abstract bool RenderFrame(HolographicFrame frame);
        #endregion

        #region Input
        protected virtual void ProcessInput()
        {
        }
        #endregion

        #region State
        protected virtual void SaveAppState()
        {
        }

        protected virtual void LoadAppState()
        {
        }
        #endregion

        #region Event Hanlders
        private void OnWindowVisibilityChanged(CoreWindow sender, VisibilityChangedEventArgs args)
        {
            this.WindowVisible = args.Visible;
        }

        private void OnWindowClosed(CoreWindow sender, CoreWindowEventArgs args)
        {
            this.WindowClosed = true;
        }

        private void OnViewActivated(CoreApplicationView sender, IActivatedEventArgs args)
        {
            // Run() won't start until the CoreWindow is activated.
            sender.CoreWindow.Activate();
        }

        private void OnSuspending(object sender, SuspendingEventArgs args)
        {
            // Save app state asynchronously after requesting a deferral. Holding a deferral
            // indicates that the application is busy performing suspending operations. Be
            // aware that a deferral may not be held indefinitely; after about five seconds,
            // the app will be forced to exit.
            var deferral = args.SuspendingOperation.GetDeferral();

            Task.Run(() =>
            {
                this.DeviceResources.Trim();
                try
                {
                    this.SaveAppState();
                }
                finally
                {
                    deferral.Complete();
                }
            });
        }

        private void OnResuming(object sender, object args)
        {
            // Restore any data or state that was unloaded on suspend. By default, data
            // and state are persisted when resuming from suspend. Note that this event
            // does not occur if the app was previously terminated.
            this.LoadAppState();
        }

        protected virtual void OnHolographicSpaceChanged(HolographicSpace holographicSpace)
        {
        }

        private void OnCameraAdded(HolographicSpace sender, HolographicSpaceCameraAddedEventArgs args)
        {
            Deferral deferral = args.GetDeferral();
            HolographicCamera holographicCamera = args.Camera;

            Task.Run(() =>
            {
                this.OnCameraAdded(holographicCamera);

                // Create device-based resources for the holographic camera and add it to the list of
                // cameras used for updates and rendering. Notes:
                //   * Since this function may be called at any time, the AddHolographicCamera function
                //     waits until it can get a lock on the set of holographic camera resources before
                //     adding the new camera. At 60 frames per second this wait should not take long.
                //   * A subsequent Update will take the back buffer from the RenderingParameters of this
                //     camera's CameraPose and use it to create the ID3D11RenderTargetView for this camera.
                //     Content can then be rendered for the HolographicCamera.
                this.DeviceResources.AddHolographicCamera(holographicCamera);

                // Holographic frame predictions will not include any information about this camera until
                // the deferral is completed.
                deferral.Complete();
            });
        }

        /// <summary>
        /// Allocate resources for the new camera and load any content specific to 
        /// that camera. Note that the render target size (in pixels) is a property
        /// of the HolographicCamera object, and can be used to create off-screen
        /// render targets that match the resolution of the HolographicCamera
        /// </summary>
        /// <param name="camera"></param>
        protected virtual void OnCameraAdded(HolographicCamera camera)
        {
        }

        private void OnCameraRemoved(HolographicSpace sender, HolographicSpaceCameraRemovedEventArgs args)
        {
            Task.Run(() =>
            {
                //
                // TODO: Asynchronously unload or deactivate content resources (not back buffer 
                //       resources) that are specific only to the camera that was removed.
                //
                this.OnCameraRemoved(args.Camera);
            });

            // Before letting this callback return, ensure that all references to the back buffer 
            // are released.
            // Since this function may be called at any time, the RemoveHolographicCamera function
            // waits until it can get a lock on the set of holographic camera resources before
            // deallocating resources for this camera. At 60 frames per second this wait should
            // not take long.
            this.DeviceResources.RemoveHolographicCamera(args.Camera);
        }

        /// <summary>
        /// Unload or deactivate content resources (not back buffer resources) that are specific only to the camera that was removed.
        /// </summary>
        /// <param name="camera">The camera</param>
        protected virtual void OnCameraRemoved(HolographicCamera camera)
        {
        }

        /// <summary>
        /// Notifies renderers that device resources need to be released.
        /// </summary>
        protected virtual void OnDeviceLost(object sender, EventArgs e)
        {
        }

        /// <summary>
        /// Notifies renderers that device resources may now be recreated.
        /// </summary>
        protected virtual void OnDeviceRestored(object sender, EventArgs e)
        {
        }
        #endregion

        #region IFrameworkView
        public virtual void Initialize(CoreApplicationView applicationView)
        {
            applicationView.Activated += OnViewActivated;
            CoreApplication.Suspending += OnSuspending;
            CoreApplication.Resuming += OnResuming;

            this.WindowVisible = true;
            this.Timer = new StepTimer();
            this.DeviceResources = new DeviceResources();
            this.DeviceResources.DeviceLost += OnDeviceLost;
            this.DeviceResources.DeviceRestored += OnDeviceRestored;
        }

        /// <summary>
        /// The Load method can be used to initialize scene resources or to load a
        /// previously saved app state.
        /// </summary>
        public virtual void Load(string entryPoint)
        {
        }

        /// <summary>
        /// This method is called after the window becomes active. It oversees the
        /// update, draw, and present loop, and also oversees window message processing.
        /// </summary>
        public void Run()
        {
            while (!this.WindowClosed)
            {
                if (this.WindowVisible && this.HolographicSpace != null)
                {
                    CoreWindow.GetForCurrentThread().Dispatcher.ProcessEvents(CoreProcessEventsOption.ProcessAllIfPresent);

                    // Check for new input state since the last frame.
                    this.ProcessInput();

                    var frame = this.CreateNextFrame();
                    if (this.Timer.FrameCount != 0 && this.RenderFrame(frame))
                        this.DeviceResources.Present(ref frame);
                }
                else
                    CoreWindow.GetForCurrentThread().Dispatcher.ProcessEvents(CoreProcessEventsOption.ProcessOneAndAllPending);
            }
        }

        /// <summary>
        /// Called when the CoreWindow object is created (or re-created).
        /// </summary>
        public virtual void SetWindow(CoreWindow window)
        {
            window.Closed += OnWindowClosed;
            window.VisibilityChanged += OnWindowVisibilityChanged;

            // Create a holographic space for the core window for the current view.
            // Presenting holographic frames that are created by this holographic space will put
            // the app into exclusive mode.
            this.HolographicSpace = HolographicSpace.CreateForCoreWindow(window);

            // The DeviceResources class uses the preferred DXGI adapter ID from the holographic
            // space (when available) to create a Direct3D device. The HolographicSpace
            // uses this ID3D11Device to create and manage device-based resources such as
            // swap chains.
            this.DeviceResources.SetHolographicSpace(this.HolographicSpace);

            // Notes on spatial tracking APIs:
            // * Stationary reference frames are designed to provide a best-fit position relative to the
            //   overall space. Individual positions within that reference frame are allowed to drift slightly
            //   as the device learns more about the environment.
            // * When precise placement of individual holograms is required, a SpatialAnchor should be used to
            //   anchor the individual hologram to a position in the real world - for example, a point the user
            //   indicates to be of special interest. Anchor positions do not drift, but can be corrected; the
            //   anchor will use the corrected position starting in the next frame after the correction has
            //   occurred.

            // Respond to camera added events by creating any resources that are specific
            // to that camera, such as the back buffer render target view.
            // When we add an event handler for CameraAdded, the API layer will avoid putting
            // the new camera in new HolographicFrames until we complete the deferral we created
            // for that handler, or return from the handler without creating a deferral. This
            // allows the app to take more than one frame to finish creating resources and
            // loading assets for the new holographic camera.
            // This function should be registered before the app creates any HolographicFrames.
            this.HolographicSpace.CameraAdded += OnCameraAdded;

            // Respond to camera removed events by releasing resources that were created for that
            // camera.
            // When the app receives a CameraRemoved event, it releases all references to the back
            // buffer right away. This includes render target views, Direct2D target bitmaps, and so on.
            // The app must also ensure that the back buffer is not attached as a render target, as
            // shown in DeviceResources.ReleaseResourcesForBackBuffer.
            this.HolographicSpace.CameraRemoved += OnCameraRemoved;

            // allow for other processing
            this.OnHolographicSpaceChanged(this.HolographicSpace);
        }

        /// <summary>
        /// Terminate events do not cause Uninitialize to be called. It will be called if your IFrameworkView
        /// class is torn down while the app is in the foreground.
        /// This method is not often used, but IFrameworkView requires it and it will be called for
        /// holographic apps.
        /// </summary>
        public void Uninitialize()
        {
        }
        #endregion

        #region IDisposable
        public virtual void Dispose()
        {
            var deviceResource = this.DeviceResources;
            if (deviceResource != null)
            {
                deviceResource.Dispose();
                this.DeviceResources = null;
            }
        }
        #endregion
    }
}
