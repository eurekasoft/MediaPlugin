//
//  Copyright 2011-2013, Xamarin Inc.
//
//    Licensed under the Apache License, Version 2.0 (the "License");
//    you may not use this file except in compliance with the License.
//    You may obtain a copy of the License at
//
//        http://www.apache.org/licenses/LICENSE-2.0
//
//    Unless required by applicable law or agreed to in writing, software
//    distributed under the License is distributed on an "AS IS" BASIS,
//    WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//    See the License for the specific language governing permissions and
//    limitations under the License.
//

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Provider;
using Plugin.Media.Abstractions;
using Plugin.Permissions;
using Android.Media;
using Android.Graphics;
using System.Collections.Generic;
namespace Plugin.Media
{
    /// <summary>
    /// Implementation for Feature
    /// </summary>
    [Android.Runtime.Preserve(AllMembers = true)]
    public class MediaImplementation : IMedia
    {
        public struct ImagesResult
        {
            public IEnumerable<MediaFile> Media { get; set; }
            public Exception Error { get; set; }
            public ImagesResult(IEnumerable<MediaFile> media,Exception error)
            {
                this.Media = media;
                this.Error = error;
            }

        }
        /// <summary>
        /// Implementation
        /// </summary>
        public MediaImplementation()
        {
            this.context = Android.App.Application.Context;
            IsCameraAvailable = context.PackageManager.HasSystemFeature(PackageManager.FeatureCamera);

            if (Build.VERSION.SdkInt >= BuildVersionCodes.Gingerbread)
                IsCameraAvailable |= context.PackageManager.HasSystemFeature(PackageManager.FeatureCameraFront);
        }

        ///<inheritdoc/>
        public Task<bool> Initialize() => Task.FromResult(true);

        /// <inheritdoc/>
        public bool IsCameraAvailable { get; }
        /// <inheritdoc/>
        public bool IsTakePhotoSupported => true;

        /// <inheritdoc/>
        public bool IsPickPhotoSupported => true;

        /// <inheritdoc/>
        public bool IsTakeVideoSupported => true;
        /// <inheritdoc/>
        public bool IsPickVideoSupported => true;

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public Intent GetPickPhotoUI()
        {
            int id = GetRequestId();
            return CreateMediaIntent(id, "image/*", Intent.ActionPick, null, tasked: false);
        }

        /// <summary>
        /// Picks a photo from the default gallery
        /// </summary>
        /// <returns>Media file or null if canceled</returns>
        public async Task<IEnumerable<MediaFile>> PickPhotoAsync(PickMediaOptions options = null)
        {
            if (!(await RequestStoragePermission()))
            {
                return null;
            }
            var media = await TakeMediaAsync("image/*", Intent.ActionOpenDocument, null);
            if (media.Error != null)
            {
                throw media.Error;
            }
         
            //check to see if we need to rotate if success
            foreach (MediaFile m_file in media.Media)
            {
                if (!string.IsNullOrWhiteSpace(m_file.Path))
                {
                    try
                    {
                        await FixOrientationAsync(m_file.Path, PhotoSize.Full, 100);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Unable to check orientation: " + ex);
                    }
                }
            }

            return media.Media;
        }

        /// <summary>
        /// Take a photo async with specified options
        /// </summary>
        /// <param name="options">Camera Media Options</param>
        /// <returns>Media file of photo or null if canceled</returns>
        public async Task<MediaFile> TakePhotoAsync(StoreCameraMediaOptions options)
        {
            if (!IsCameraAvailable)
                throw new NotSupportedException();

            if (!(await RequestCameraPermission()))
            {
                return null;
            }

            var media = await TakeMediaAsync("image/*", MediaStore.ActionImageCapture, null);
            if(media.Error != null)
            {
                throw media.Error;
            }

            if(media.Media.Count() > 0)
            {
                VerifyOptions(options);
                MediaFile photo = media.Media.ToList<MediaFile>()[0];
                if (options == null)
                    return photo;

                //check to see if we need to rotate if success
                foreach (MediaFile m_file in media.Media)
                {
                    if (!string.IsNullOrWhiteSpace(m_file.Path))
                    {
                        try
                        {
                            await FixOrientationAsync(m_file.Path, options.PhotoSize, options.CompressionQuality);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("Unable to check orientation: " + ex);
                        }
                    }
                }

                return photo;
            }
            return null;
        }

        private readonly Context context;
        private int requestId;
        private TaskCompletionSource<ImagesResult> completionSource;


        async Task<bool> RequestStoragePermission()
        {
            var status = await CrossPermissions.Current.CheckPermissionStatusAsync(Permissions.Abstractions.Permission.Storage);
            if (status != Permissions.Abstractions.PermissionStatus.Granted)
            {
                Console.WriteLine("Does not have storage permission granted, requesting.");
                var results = await CrossPermissions.Current.RequestPermissionsAsync(Permissions.Abstractions.Permission.Storage);
                int x = 5;
                if (results.ContainsKey(Permissions.Abstractions.Permission.Storage) &&
                    results[Permissions.Abstractions.Permission.Storage] != Permissions.Abstractions.PermissionStatus.Granted)
                {
                    Console.WriteLine("Storage permission Denied.");
                    return false;
                }
            }

            return true;
        }

        async Task<bool> RequestCameraPermission()
        {
            var status = await CrossPermissions.Current.CheckPermissionStatusAsync(Permissions.Abstractions.Permission.Camera);
            if (status != Permissions.Abstractions.PermissionStatus.Granted)
            {
                Console.WriteLine("Does not have camera permission granted, requesting.");
                var results = await CrossPermissions.Current.RequestPermissionsAsync(Permissions.Abstractions.Permission.Camera);
                if (results.ContainsKey(Permissions.Abstractions.Permission.Camera) &&
                    results[Permissions.Abstractions.Permission.Camera] != Permissions.Abstractions.PermissionStatus.Granted)
                {
                    Console.WriteLine("Camera permission Denied.");
                    return false;
                }
            }

            return true;
        }

        private void VerifyOptions(StoreMediaOptions options)
        {
            if (options == null)
                throw new ArgumentNullException("options");
            if (System.IO.Path.IsPathRooted(options.Directory))
                throw new ArgumentException("options.Directory must be a relative path", "options");
        }

        private Intent CreateMediaIntent(int id, string type, string action, StoreMediaOptions options, bool tasked = true)
        {
            Intent pickerIntent = new Intent(this.context, typeof(MediaPickerActivity));
            pickerIntent.PutExtra(MediaPickerActivity.ExtraId, id);
            pickerIntent.PutExtra(MediaPickerActivity.ExtraType, type);
            pickerIntent.PutExtra(MediaPickerActivity.ExtraAction, action);
            pickerIntent.PutExtra(MediaPickerActivity.ExtraTasked, tasked);

            if (options != null)
            {
                pickerIntent.PutExtra(MediaPickerActivity.ExtraPath, options.Directory);
                pickerIntent.PutExtra(MediaStore.Images.ImageColumns.Title, options.Name);

                var cameraOptions = (options as StoreCameraMediaOptions);
                if (cameraOptions != null)
                {
                    if (cameraOptions.DefaultCamera == CameraDevice.Front)
                    {
                        pickerIntent.PutExtra("android.intent.extras.CAMERA_FACING", 1);
                    }
                    pickerIntent.PutExtra(MediaPickerActivity.ExtraSaveToAlbum, cameraOptions.SaveToAlbum);
                }
                var vidOptions = (options as StoreVideoOptions);
                if (vidOptions != null)
                {
                    if (vidOptions.DefaultCamera == CameraDevice.Front)
                    {
                        pickerIntent.PutExtra("android.intent.extras.CAMERA_FACING", 1);
                    }
                    pickerIntent.PutExtra(MediaStore.ExtraDurationLimit, (int)vidOptions.DesiredLength.TotalSeconds);
                    pickerIntent.PutExtra(MediaStore.ExtraVideoQuality, (int)vidOptions.Quality);
                }
            }
            //pickerIntent.SetFlags(ActivityFlags.ClearTop);
            pickerIntent.SetFlags(ActivityFlags.NewTask);
            return pickerIntent;
        }

        private int GetRequestId()
        {
            int id = this.requestId;
            if (this.requestId == Int32.MaxValue)
                this.requestId = 0;
            else
                this.requestId++;

            return id;
        }

        private Task<ImagesResult> TakeMediaAsync(string type, string action, StoreMediaOptions options)
        {
            int id = GetRequestId();

            var ntcs = new TaskCompletionSource<ImagesResult>(id);
            if (Interlocked.CompareExchange(ref this.completionSource, ntcs, null) != null)
                throw new InvalidOperationException("Only one operation can be active at a time");
           
            this.context.StartActivity(CreateMediaIntent(id, type, action, options));

            EventHandler<MediaPickedEventArgs> handler = null;
            handler = (s, e) =>
            {
                var tcs = Interlocked.Exchange(ref this.completionSource, null);
                //remove any existing handler
                MediaPickerActivity.MediaPicked -= handler;

                if (e.RequestId != id)
                    return;

                if (e.Error != null)
                {
                    tcs.SetResult(new ImagesResult(null,e.Error));
                }
                    
                else if (e.IsCanceled)
                    tcs.SetResult(new ImagesResult(Enumerable.Empty<MediaFile>(), null));
                else
                    tcs.SetResult(new ImagesResult(e.Media,null));
            };

            MediaPickerActivity.MediaPicked += handler;

            return completionSource.Task;
        }

        /// <summary>
        ///  Rotate an image if required and saves it back to disk.
        /// </summary>
        /// <param name="filePath">The file image path</param>
        /// <param name="photoSize">Photo size to go to.</param>
        /// <returns>True if rotation or compression occured, else false</returns>
        public Task<bool> FixOrientationAsync(string filePath, PhotoSize photoSize, int quality)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return Task.FromResult(false);

            try
            {
                return Task.Run(() =>
                {
                    try
                    {
                        var rotation = GetRotation(filePath);

                        if (rotation == 0)
                            return false;

                        using (var originalImage = BitmapFactory.DecodeFile(filePath))
                        {
                            //if we need to rotate then go for it.
                            //then compresse it if needed
                            if (rotation != 0)
                            {
                                var matrix = new Matrix();
                                matrix.PostRotate(rotation);
                                using (var rotatedImage = Bitmap.CreateBitmap(originalImage, 0, 0, originalImage.Width, originalImage.Height, matrix, true))
                                {
                                    using (var stream = File.Open(filePath, FileMode.Create, FileAccess.ReadWrite))
                                    {
                                        rotatedImage.Compress(Bitmap.CompressFormat.Jpeg, quality, stream);
                                        stream.Close();
                                    }
                                    
                                    rotatedImage.Recycle();
                                }
                                originalImage.Recycle();
                                // Dispose of the Java side bitmap.
                                GC.Collect();
                                return true;
                            }
                            return false;
                        }
                    }
                    catch (Exception ex)
                    {
#if DEBUG
                        throw ex;
#else
                        return false;
#endif
                    }
                });
            }
            catch (Exception ex)
            {
#if DEBUG
                throw ex;
#else
                return Task.FromResult(false);
#endif
            }
        }

        /// <summary>
        /// Resize Image Async
        /// </summary>
        /// <param name="filePath">The file image path</param>
        /// <param name="photoSize">Photo size to go to.</param>
        /// <returns>True if rotation or compression occured, else false</returns>
        public Task<bool> ResizeAsync(string filePath, PhotoSize photoSize, int quality)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return Task.FromResult(false);

            try
            {
                return Task.Run(() =>
                {
                    try
                    {
                        

                        if (photoSize == PhotoSize.Full)
                            return false;

                        var percent = 1.0f;
                        switch (photoSize)
                        {
                            case PhotoSize.Large:
                                percent = .75f;
                                break;
                            case PhotoSize.Medium:
                                percent = .5f;
                                break;
                            case PhotoSize.Small:
                                percent = .25f;
                                break;
                        }

                        using (var originalImage = BitmapFactory.DecodeFile(filePath))
                        {
                            
                            using (var compressedImage = Bitmap.CreateScaledBitmap(originalImage, (int)(originalImage.Width * percent), (int)(originalImage.Height * percent), false))
                            {
                                using (var stream = File.Open(filePath, FileMode.Create, FileAccess.ReadWrite))
                                {
                                   
                                    compressedImage.Compress(Bitmap.CompressFormat.Jpeg, quality, stream);
                                    stream.Close();
                                }

                                compressedImage.Recycle();

                            }
                            originalImage.Recycle();

                            // Dispose of the Java side bitmap.
                            GC.Collect();
                            return true;
                        }
                    }
                    catch (Exception ex)
                    {
#if DEBUG
                        throw ex;
#else
                        return false;
#endif
                    }
                });
            }
            catch (Exception ex)
            {
#if DEBUG
                throw ex;
#else
                return Task.FromResult(false);
#endif
            }
        }


        static int GetRotation(string filePath)
        {
            try
            {
                using (var ei = new ExifInterface(filePath))
                {
                    var orientation = (Orientation)ei.GetAttributeInt(ExifInterface.TagOrientation, (int)Orientation.Normal);

                    switch (orientation)
                    {
                        case Orientation.Rotate90:
                            return 90;
                        case Orientation.Rotate180:
                            return 180;
                        case Orientation.Rotate270:
                            return 270;
                        default:
                            return 0;
                    }
                }
            }
            catch (Exception ex)
            {
#if DEBUG
                throw ex;
#else
                return 0;
#endif
            }
        }

    }


}
