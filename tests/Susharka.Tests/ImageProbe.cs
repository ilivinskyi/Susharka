using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Susharka.Core;

namespace Susharka.Tests;

public class ImageIOTests
{
    [Fact]
    public void Saved_png_loads_back_without_locking_the_file()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            var path = Path.Combine(dir.FullName, "shot.png");
            var bmp = BitmapSource.Create(40, 20, 96, 96, PixelFormats.Bgr32, null, new byte[40 * 20 * 4], 40 * 4);
            ImageIO.Save(bmp, path);

            var full = ImageIO.Load(path);
            var thumb = ImageIO.Load(path, 10);

            Assert.NotNull(full);
            Assert.Equal(40, full!.PixelWidth);
            Assert.Equal(10, thumb!.PixelWidth);
            Assert.Equal((40, 20), ImageIO.PixelSize(path));
            File.Delete(path); // would throw if Load kept it open
        }
        finally
        {
            dir.Delete(true);
        }
    }
}
