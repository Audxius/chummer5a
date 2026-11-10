/* This file is part of Chummer5a. Licensed GPLv3; see LICENSE. */
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace Chummer.DataEditing
{
    public sealed class XmlAmendmentPreview
    {
        public string Base { get; set; }
        public string Amendment { get; set; }
        public string Result { get; set; }
        public string Changes { get; set; }
    }

    public static class XmlAmendmentRules
    {
        public static Task<XmlAmendmentPreview> PreviewAsync(string original, string amendment, CancellationToken token = default)
            => Task.Run(() => new XmlAmendmentWorker().PreviewAsync(original, amendment, token), token);

        public static async Task<string> FormatAsync(string content, CancellationToken token = default)
        {
            var document = new XmlDocument { XmlResolver = null };
            await document.LoadXmlStandardAsync(content, token: token).ConfigureAwait(false);
            using (var stream = new MemoryStream())
            {
                using (var writer = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true, CloseOutput = false })) document.Save(writer);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }
    }

    internal sealed partial class XmlAmendmentWorker
    {
        public async Task<XmlAmendmentPreview> PreviewAsync(string original, string amendment, CancellationToken token)
        {
            var result = new XmlDocument { XmlResolver = null };
            var changes = new XmlDocument { XmlResolver = null };
            await result.LoadXmlStandardAsync(original, token: token).ConfigureAwait(false);
            await changes.LoadXmlStandardAsync(amendment, token: token).ConfigureAwait(false);
            if (result.DocumentElement?.Name != "chummer" || changes.DocumentElement?.Name != "chummer")
                throw new XmlException("Game data and amendments must have a <chummer> root element.");
            foreach (XmlNode operation in changes.SelectNodes("/chummer/*"))
            {
                token.ThrowIfCancellationRequested();
                XmlManager.AmendNodeChildren(result, operation, "/chummer", token: token);
            }
            return new XmlAmendmentPreview
            {
                Base = await XmlAmendmentRules.FormatAsync(original, token).ConfigureAwait(false),
                Amendment = await XmlAmendmentRules.FormatAsync(amendment, token).ConfigureAwait(false),
                Result = await XmlAmendmentRules.FormatAsync(result.OuterXml, token).ConfigureAwait(false),
                Changes = await GenerateCleanDiffAsync(original, result.OuterXml, token).ConfigureAwait(false)
            };
        }
    }
}
