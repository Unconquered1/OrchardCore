using OrchardCore.ResourceManagement;

namespace OrchardCore.Themes.MyTheTheme
{
    public class ResourceManifest : IResourceManifestProvider
    {
        public void BuildManifests(IResourceManifestBuilder builder)
        {
            var manifest = builder.Add();

            manifest
                .DefineStyle("MyTheTheme-bootstrap-oc")
                .SetUrl("~/MyTheTheme/css/bootstrap-oc.min.css", "~/MyTheTheme/css/bootstrap-oc.css")
                .SetVersion("1.0.0");
				
        }
    }
}
