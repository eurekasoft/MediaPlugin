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
using Android.App;
using Android.Content;
using Android.Database;
using Android.OS;
using Android.Provider;
using Environment = Android.OS.Environment;
using Path = System.IO.Path;
using Uri = Android.Net.Uri;
using Plugin.Media.Abstractions;
using Android.Net;
using System.Collections.Generic;

namespace Plugin.Media
{
    /// <summary>
    /// Picker
    /// </summary>
    [Activity(ConfigurationChanges = Android.Content.PM.ConfigChanges.Orientation | Android.Content.PM.ConfigChanges.ScreenSize)]
    [Android.Runtime.Preserve(AllMembers = true)]
    public class MediaPickerActivity
        : Activity, Android.Media.MediaScannerConnection.IOnScanCompletedListener
    {
        internal const string ExtraPath = "path";
        internal const string ExtraLocation = "location";
        internal const string ExtraType = "type";
        internal const string ExtraId = "id";
        internal const string ExtraAction = "action";
        internal const string ExtraTasked = "tasked";
        internal const string ExtraSaveToAlbum = "album_save";
        internal const string ExtraFront = "android.intent.extras.CAMERA_FACING";

        internal static event EventHandler<MediaPickedEventArgs> MediaPicked;

        private int id;
        private int front;
        private string title;
        private string description;
        private string type;

        /// <summary>
        /// The user's destination path.
        /// </summary>
        private Uri path;
        private bool isPhoto;
        private bool saveToAlbum;
        private string action;

        private int seconds;
        private VideoQuality quality;

        private bool tasked;
        /// <summary>
        /// OnSaved
        /// </summary>
        /// <param name="outState"></param>
        protected override void OnSaveInstanceState(Bundle outState)
        {
            outState.PutBoolean("ran", true);
            outState.PutString(MediaStore.MediaColumns.Title, this.title);
            outState.PutString(MediaStore.Images.ImageColumns.Description, this.description);
            outState.PutInt(ExtraId, this.id);
            outState.PutString(ExtraType, this.type);
            outState.PutString(ExtraAction, this.action);
            outState.PutInt(MediaStore.ExtraDurationLimit, this.seconds);
            outState.PutInt(MediaStore.ExtraVideoQuality, (int)this.quality);
            outState.PutBoolean(ExtraSaveToAlbum, saveToAlbum);
            outState.PutBoolean(ExtraTasked, this.tasked);
            outState.PutInt(ExtraFront, this.front);

            if (this.path != null)
                outState.PutString(ExtraPath, this.path.Path);

            base.OnSaveInstanceState(outState);
        }

        /// <summary>
        /// OnCreate
        /// </summary>
        /// <param name="savedInstanceState"></param>
        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            Bundle b = (savedInstanceState ?? Intent.Extras);

            bool ran = b.GetBoolean("ran", defaultValue: false);

            this.title = b.GetString(MediaStore.MediaColumns.Title);
            this.description = b.GetString(MediaStore.Images.ImageColumns.Description);

            this.tasked = b.GetBoolean(ExtraTasked);
            this.id = b.GetInt(ExtraId, 0);
            this.type = b.GetString(ExtraType);
            this.front = b.GetInt(ExtraFront);
            if (this.type == "image/*")
                this.isPhoto = true;

            this.action = b.GetString(ExtraAction);
            Intent pickIntent = null;
            try
            {
                pickIntent = new Intent(this.action);
                //select image / video
                if (this.action == Intent.ActionGetContent)
                {
                    //allow pick multiple images.
                    pickIntent.PutExtra(Intent.ExtraAllowMultiple, true);
                    pickIntent.SetType(type);
                }
                //take photo / video setup
                if (!ran)
                    StartActivityForResult(pickIntent, this.id);
            }
            catch (Exception ex)
            {
                OnMediaPicked(new MediaPickedEventArgs(this.id, ex));
            }
            finally
            {
                if (pickIntent != null)
                    pickIntent.Dispose();
            }
        }

        internal static Task<MediaPickedEventArgs> GetMediaFileAsync(Context context, int requestCode, string action, bool isPhoto, ref Uri path, List<Uri> data, bool saveToAlbum)
        {
            Task<List<Tuple<string, bool>>> pathFuture;
            string originalPath = null;
            List<MediaFile> m_files = new List<MediaFile>();

            if (action != Intent.ActionPick)
            {

                originalPath = path.Path;
                
                // Not all camera apps respect EXTRA_OUTPUT, some will instead
                // return a content or file uri from data.
                if (data != null && data[0].Path != originalPath)
                {
                    originalPath = data[0].ToString();
                    string currentPath = path.Path;
                    List<Tuple<string, bool>> tup_list = new List<Tuple<string, bool>>();
                    pathFuture = TryMoveFileAsync(context, data[0], path, isPhoto, false).ContinueWith(t =>
                    new List<Tuple<string, bool>>() {new Tuple<string,bool>(t.Result ? currentPath : null, false) });
                }
                else
                {
                    pathFuture = TaskFromResult(new List<Tuple<string, bool>> { new Tuple<string,bool>(path.Path, false) });
                }
            }
            //Select photo video
            if (data != null)
            {
                //path = data;
                pathFuture = GetFileForUriAsync(context, data, isPhoto, false);
            }
            //failed to get data
            else
                pathFuture = TaskFromResult<List<Tuple<string, bool>>>(null);

            return pathFuture.ContinueWith(t =>
            {
                foreach (Tuple<string, bool> tup in t.Result)
                {
                    string resultPath = tup.Item1;
                    var aPath = originalPath;
                    if (resultPath != null && File.Exists(tup.Item1))
                    {
                        m_files.Add(new MediaFile(resultPath, () =>
                          {
                              return File.OpenRead(resultPath);
                          }, deletePathOnDispose: tup.Item2, dispose: (dis) =>
                          {
                              if (tup.Item2)
                              {
                                  try
                                  {
                                      File.Delete(tup.Item1);
                                      // We don't really care if this explodes for a normal IO reason.
                                  }
                                  catch (UnauthorizedAccessException)
                                  {
                                  }
                                  catch (DirectoryNotFoundException)
                                  {
                                  }
                                  catch (IOException)
                                  {
                                  }
                              }
                          }));

                    }
                    else
                        return new MediaPickedEventArgs(requestCode, new MediaFileNotFoundException(originalPath));
                }
                return new MediaPickedEventArgs(requestCode, false, m_files);
            });

        }

        /// <summary>
        /// OnActivity Result
        /// </summary>
        /// <param name="requestCode"></param>
        /// <param name="resultCode"></param>
        /// <param name="data"></param>
        protected override async void OnActivityResult(int requestCode, Result resultCode, Intent data)
        {
            base.OnActivityResult(requestCode, resultCode, data);
            if (this.tasked)
            {
                Task<MediaPickedEventArgs> future;
                //handle cancellation
                if (resultCode == Result.Canceled)
                {
                    future = TaskFromResult(new MediaPickedEventArgs(requestCode, isCanceled: true));
                    Finish();
                    future.ContinueWith(t => OnMediaPicked(t.Result));
                }
                else
                {
                    List<Uri> paths = new List<Uri>();
                    if (data != null)
                    {
                        ClipData clipData = data.ClipData;
                        if (clipData != null)
                        {

                            for (int i = 0; i < clipData.ItemCount; i++)
                            {

                                if (i > 19)
                                {
                                    break;
                                }
                                ClipData.Item item = clipData.GetItemAt(i);
                                paths.Add(item.Uri);
                            }

                        }
                        else
                        {
                            paths.Add(data.Data);
                        }
                        //transform file paths to mediafiles
                        if ((int)Build.VERSION.SdkInt >= 22)
                        {
                            //data.d
                            var e = await GetMediaFileAsync(this, requestCode, this.action, this.isPhoto, ref this.path, paths, false);
                            OnMediaPicked(e);
                            Finish();
                        }
                        else
                        {
                            future = GetMediaFileAsync(this, requestCode, this.action, this.isPhoto, ref this.path, paths, false);

                            Finish();

                            future.ContinueWith(t => OnMediaPicked(t.Result));
                        }
                    }
                }
            }

            else
            {
                if (resultCode == Result.Canceled)
                    SetResult(Result.Canceled);
                else
                {
                    Intent resultData = new Intent();
                    resultData.PutExtra("MediaFile", (data != null) ? data.Data : null);
                    resultData.PutExtra("path", this.path);
                    resultData.PutExtra("isPhoto", this.isPhoto);
                    resultData.PutExtra("action", this.action);
                    resultData.PutExtra(ExtraSaveToAlbum, this.saveToAlbum);
                    SetResult(Result.Ok, resultData);
                }

                Finish();
            }
        }

        public static Task<bool> TryMoveFileAsync(Context context, Uri url, Uri path, bool isPhoto, bool saveToAlbum)
        {
            string moveTo = GetLocalPath(path);
            List<Uri> l_url = new List<Uri>() { url };
            return GetFileForUriAsync(context, l_url, isPhoto, false).ContinueWith(t =>
            {

                if (t.Result[0].Item1 == null)
                    return false;
                File.Delete(moveTo);
                File.Move(t.Result[0].Item1, moveTo);

                if (url.Scheme == "content")
                    context.ContentResolver.Delete(url, null, null);

                return true;
            }, TaskScheduler.Default);
        }

        internal static Task<List<Tuple<string, bool>>> GetFileForUriAsync(Context context, List<Uri> paths, bool isPhoto, bool saveToAlbum)
        {
           var task = Task.Factory.StartNew(() =>
           {
               List<Tuple<string, bool>> result = new List<Tuple<string, bool>>();
               foreach (Uri path in paths)
               {
                   if (path.Scheme == "file")
                   {
                       result.Add(new Tuple<string, bool>(new System.Uri(path.ToString()).LocalPath, false));
                   }
                   else if (path.Scheme == "content")
                   {
                       ICursor cursor = null;
                       try
                       {
                           string[] proj = null;
                           if ((int)Build.VERSION.SdkInt >= 22)
                               proj = new[] { MediaStore.MediaColumns.Data };

                           cursor = context.ContentResolver.Query(path, proj, null, null, null);
                           if (cursor == null || !cursor.MoveToNext())
                               result.Add(new Tuple<string, bool>(null, false));
                           else
                           {
                               int column = cursor.GetColumnIndex(MediaStore.MediaColumns.Data);
                               string contentPath = null;

                               if (column != -1)
                                   contentPath = cursor.GetString(column);
                               // If they don't follow the "rules", try to copy the file locally
                               if (contentPath == null || !contentPath.StartsWith("file"))
                               {
                                   Uri outputPath = GetOutputMediaFile(context, "temp", null, isPhoto, false);
                                   try
                                   {
                                       using (Stream input = context.ContentResolver.OpenInputStream(path))
                                       using (Stream output = File.Create(outputPath.Path))
                                           input.CopyTo(output);
                                       contentPath = outputPath.Path;
                                   }
                                   catch (Java.IO.FileNotFoundException)
                                   {
                                       // If there's no data associated with the uri, we don't know
                                       // how to open this. contentPath will be null which will trigger
                                       // MediaFileNotFoundException.
                                   }
                               }
                               result.Add(new Tuple<string, bool>(contentPath, false));
                           }
                       }
                       finally
                       {
                           if (cursor != null)
                           {
                               cursor.Close();
                               cursor.Dispose();
                           }
                       }
                   }
                   else
                   {
                       result.Add(new Tuple<string, bool>(null, false));
                   }
               }
               return result;
           }, CancellationToken.None, TaskCreationOptions.None, TaskScheduler.Default);
           return task;
        }



        public static Uri GetOutputMediaFile(Context context, string subdir, string name, bool isPhoto, bool saveToAlbum)
        {
            subdir = subdir ?? String.Empty;

            if (String.IsNullOrWhiteSpace(name))
            {
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                if (isPhoto)
                    name = "IMG_" + timestamp + ".jpg";
                else
                    name = "VID_" + timestamp + ".mp4";
            }

            string mediaType = (isPhoto) ? Environment.DirectoryPictures : Environment.DirectoryMovies;
            var directory = saveToAlbum ? Environment.GetExternalStoragePublicDirectory(mediaType) : context.GetExternalFilesDir(mediaType);
            using (Java.IO.File mediaStorageDir = new Java.IO.File(directory, subdir))
            {
                if (!mediaStorageDir.Exists())
                {
                    if (!mediaStorageDir.Mkdirs())
                        throw new IOException("Couldn't create directory, have you added the WRITE_EXTERNAL_STORAGE permission?");

                    if (!saveToAlbum)
                    {
                        // Ensure this media doesn't show up in gallery apps
                        using (Java.IO.File nomedia = new Java.IO.File(mediaStorageDir, ".nomedia"))
                            nomedia.CreateNewFile();
                    }
                }

                return Uri.FromFile(new Java.IO.File(GetUniquePath(mediaStorageDir.Path, name, isPhoto)));
            }
        }

        private static string GetUniquePath(string folder, string name, bool isPhoto)
        {
            string ext = Path.GetExtension(name);
            if (ext == String.Empty)
                ext = ((isPhoto) ? ".jpg" : ".mp4");

            name = Path.GetFileNameWithoutExtension(name);

            string nname = name + ext;
            int i = 1;
            while (File.Exists(Path.Combine(folder, nname)))
                nname = name + "_" + (i++) + ext;

            return Path.Combine(folder, nname);
        }

        private static string GetLocalPath(Uri uri)
        {
            return new System.Uri(uri.ToString()).LocalPath;
        }

        private static Task<T> TaskFromResult<T>(T result)
        {
            var tcs = new TaskCompletionSource<T>();
            tcs.SetResult(result);
            return tcs.Task;
        }

        private static void OnMediaPicked(MediaPickedEventArgs e)
        {
            var picked = MediaPicked;
            if (picked != null)
                picked(null, e);
        }

        public void OnScanCompleted(string path, Uri uri)
        {
            Console.WriteLine("scan complete: " + path);
        }
    }

    internal class MediaPickedEventArgs
        : EventArgs
    {
        public MediaPickedEventArgs(int id, Exception error)
        {
            if (error == null)
                throw new ArgumentNullException("error");

            RequestId = id;
            Error = error;
        }

        public MediaPickedEventArgs(int id, bool isCanceled, IEnumerable<MediaFile> media = null)
        {
            RequestId = id;
            IsCanceled = isCanceled;
            if (!IsCanceled && media == null)
                throw new ArgumentNullException("media");

            Media = media;
        }

        public int RequestId
        {
            get;
            private set;
        }

        public bool IsCanceled
        {
            get;
            private set;
        }

        public Exception Error
        {
            get;
            private set;
        }

        public IEnumerable<MediaFile> Media
        {
            get;
            private set;
        }

        public Task<IEnumerable<MediaFile>> ToTask()
        {
            var tcs = new TaskCompletionSource<IEnumerable<MediaFile>>();

            if (IsCanceled)
                tcs.SetResult(null);
            else if (Error != null)
                tcs.SetResult(null);
            else
                tcs.SetResult(Media);

            return tcs.Task;
        }
    }
}