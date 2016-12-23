
using System;
using Plugin.Media;
using Plugin.Media.Abstractions;
namespace Plugin.Media
{
    /// <summary>
    /// Cross platform Media implemenations
    /// </summary>
    public class CrossMedia
    {
        static Lazy<Abstractions.IMedia> Implementation = new Lazy<Abstractions.IMedia>(() => CreateMedia(), System.Threading.LazyThreadSafetyMode.PublicationOnly);

        /// <summary>
        /// Current settings to use
        /// </summary>
        public static Abstractions.IMedia Current
        {
            get
            {
                var ret = Implementation.Value;
                if (ret == null)
                {
                    throw NotImplementedInReferenceAssembly();
                }
                return ret;
            }
        }

        static Abstractions.IMedia CreateMedia()
        {
#if PORTABLE
            return null;
#else
            return new MediaImplementation();
#endif
        }

        internal static Exception NotImplementedInReferenceAssembly()
        {
            return new NotImplementedException("This functionality is not implemented in the portable version of this assembly.  You should reference the NuGet package from your main application project in order to reference the platform-specific implementation.");
        }
    }
}
