# Images

`ImageContent` represents image inputs for vision-capable Copilot models. The
SDK supports base64 PNG, JPEG, GIF, and WebP content in portable context calls.

```csharp
var context = new Context
{
    Messages =
    [
        new UserMessage(
        [
            new TextContent("Describe this image."),
            ImageContentFactory.FromFile("diagram.png"),
        ]),
    ],
};

var message = await client.CompleteAsync(context, new CompletionOptions
{
    Model = "gpt-5.4",
});
```

`ImageContentFactory.FromFile` rejects unsupported extensions and files larger
than 20 MB by default. `FromBytes` accepts raw bytes plus an explicit media type.

When the selected model does not advertise vision support, portable calls
replace images with `[image omitted: model does not support vision]`, continue
the request, and attach an `image_dropped` diagnostic.

## Image generation

Copilot currently exposes vision/image-input capability through chat and
Responses models, but the API surface used by this repo does not expose image
generation endpoints. Keep image generation out of scope unless Copilot adds a
supported model and endpoint.
