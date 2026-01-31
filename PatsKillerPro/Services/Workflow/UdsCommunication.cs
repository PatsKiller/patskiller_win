using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PatsKillerPro.Services.Workflow
{
    /// <summary>
    /// UDS communication helper with NRC-aware error classification.
    /// Wraps low-level J2534 communication for use in workflow operations.
    /// </summary>
    public sealed class UdsCommunication
    {
        private readonly Func<uint, byte[], Task<UdsResponse>> _sendAsync;
        private readonly Action<string>? _log;

        /// <summary>
        /// Creates a UDS communication helper.
        /// </summary>
        /// <param name="sendAsync">Function to send UDS message: (moduleAddress, data) => response</param>
        /// <param name="log">Optional logging callback</param>
        public UdsCommunication(Func<uint, byte[], Task<UdsResponse>> sendAsync, Action<string>? log = null)
        {
            _sendAsync = sendAsync ?? throw new ArgumentNullException(nameof(sendAsync));
            _log = log;
        }

        #region UDS Services

        /// <summary>
        /// Sends Diagnostic Session Control (0x10).
        /// </summary>
        public async Task<UdsResponse> DiagnosticSessionControlAsync(
            uint moduleAddress, DiagnosticSessionType session, CancellationToken ct = default)
        {
            _log?.Invoke($"DiagnosticSessionControl: Module=0x{moduleAddress:X3}, Session={session}");
            var request = new byte[] { 0x10, (byte)session };
            return await SendWithClassificationAsync(moduleAddress, request, NrcContext.DiagnosticSession, ct);
        }

        /// <summary>
        /// Requests Security Access Seed (0x27 01/03/etc).
        /// </summary>
        public async Task<UdsResponse> SecurityAccessRequestSeedAsync(
            uint moduleAddress, byte level = 0x01, CancellationToken ct = default)
        {
            _log?.Invoke($"SecurityAccessRequestSeed: Module=0x{moduleAddress:X3}, Level=0x{level:X2}");
            var request = new byte[] { 0x27, level };
            return await SendWithClassificationAsync(moduleAddress, request, NrcContext.SecurityAccess, ct);
        }

        /// <summary>
        /// Sends Security Access Key (0x27 02/04/etc).
        /// </summary>
        public async Task<UdsResponse> SecurityAccessSendKeyAsync(
            uint moduleAddress, byte[] key, byte level = 0x02, CancellationToken ct = default)
        {
            _log?.Invoke($"SecurityAccessSendKey: Module=0x{moduleAddress:X3}, Level=0x{level:X2}, Key={BytesToHex(key)}");
            var request = new byte[2 + key.Length];
            request[0] = 0x27;
            request[1] = level;
            Array.Copy(key, 0, request, 2, key.Length);
            return await SendWithClassificationAsync(moduleAddress, request, NrcContext.SecurityAccess, ct);
        }

        /// <summary>
        /// Reads Data By Identifier (0x22).
        /// </summary>
        public async Task<UdsResponse> ReadDataByIdentifierAsync(
            uint moduleAddress, ushort did, CancellationToken ct = default)
        {
            _log?.Invoke($"ReadDataByIdentifier: Module=0x{moduleAddress:X3}, DID=0x{did:X4}");
            var request = new byte[] { 0x22, (byte)(did >> 8), (byte)(did & 0xFF) };
            return await SendWithClassificationAsync(moduleAddress, request, NrcContext.Default, ct);
        }

        /// <summary>
        /// Writes Data By Identifier (0x2E).
        /// </summary>
        public async Task<UdsResponse> WriteDataByIdentifierAsync(
            uint moduleAddress, ushort did, byte[] data, CancellationToken ct = default)
        {
            _log?.Invoke($"WriteDataByIdentifier: Module=0x{moduleAddress:X3}, DID=0x{did:X4}");
            var request = new byte[3 + data.Length];
            request[0] = 0x2E;
            request[1] = (byte)(did >> 8);
            request[2] = (byte)(did & 0xFF);
            Array.Copy(data, 0, request, 3, data.Length);
            return await SendWithClassificationAsync(moduleAddress, request, NrcContext.DataTransfer, ct);
        }

        /// <summary>
        /// Starts a Routine (0x31 01).
        /// </summary>
        public async Task<UdsResponse> RoutineControlStartAsync(
            uint moduleAddress, ushort routineId, byte[]? data = null, CancellationToken ct = default)
        {
            _log?.Invoke($"RoutineControlStart: Module=0x{moduleAddress:X3}, Routine=0x{routineId:X4}");
            var dataLen = data?.Length ?? 0;
            var request = new byte[4 + dataLen];
            request[0] = 0x31;
            request[1] = 0x01; // Start Routine
            request[2] = (byte)(routineId >> 8);
            request[3] = (byte)(routineId & 0xFF);
            if (data != null)
                Array.Copy(data, 0, request, 4, data.Length);
            return await SendWithClassificationAsync(moduleAddress, request, NrcContext.RoutineControl, ct);
        }

        /// <summary>
        /// Stops a Routine (0x31 02).
        /// </summary>
        public async Task<UdsResponse> RoutineControlStopAsync(
            uint moduleAddress, ushort routineId, CancellationToken ct = default)
        {
            _log?.Invoke($"RoutineControlStop: Module=0x{moduleAddress:X3}, Routine=0x{routineId:X4}");
            var request = new byte[] { 0x31, 0x02, (byte)(routineId >> 8), (byte)(routineId & 0xFF) };
            return await SendWithClassificationAsync(moduleAddress, request, NrcContext.RoutineControl, ct);
        }

        /// <summary>
        /// Requests Routine Results (0x31 03).
        /// </summary>
        public async Task<UdsResponse> RoutineControlResultsAsync(
            uint moduleAddress, ushort routineId, CancellationToken ct = default)
        {
            _log?.Invoke($"RoutineControlResults: Module=0x{moduleAddress:X3}, Routine=0x{routineId:X4}");
            var request = new byte[] { 0x31, 0x03, (byte)(routineId >> 8), (byte)(routineId & 0xFF) };
            return await SendWithClassificationAsync(moduleAddress, request, NrcContext.RoutineControl, ct);
        }

        /// <summary>
        /// Sends Tester Present (0x3E 00).
        /// </summary>
        public async Task<UdsResponse> TesterPresentAsync(
            uint moduleAddress, bool suppressResponse = false, CancellationToken ct = default)
        {
            var request = new byte[] { 0x3E, (byte)(suppressResponse ? 0x80 : 0x00) };
            return await SendWithClassificationAsync(moduleAddress, request, NrcContext.Default, ct);
        }

        /// <summary>
        /// Clears DTCs (0x14).
        /// </summary>
        public async Task<UdsResponse> ClearDtcAsync(
            uint moduleAddress, uint groupOfDtc = 0xFFFFFF, CancellationToken ct = default)
        {
            _log?.Invoke($"ClearDTC: Module=0x{moduleAddress:X3}");
            var request = new byte[] { 0x14, (byte)(groupOfDtc >> 16), (byte)(groupOfDtc >> 8), (byte)groupOfDtc };
            return await SendWithClassificationAsync(moduleAddress, request, NrcContext.Default, ct);
        }

        /// <summary>
        /// Reads DTCs (0x19).
        /// </summary>
        public async Task<UdsResponse> ReadDtcAsync(
            uint moduleAddress, byte reportType = 0x02, byte statusMask = 0xFF, CancellationToken ct = default)
        {
            _log?.Invoke($"ReadDTC: Module=0x{moduleAddress:X3}");
            var request = new byte[] { 0x19, reportType, statusMask };
            return await SendWithClassificationAsync(moduleAddress, request, NrcContext.Default, ct);
        }

        /// <summary>
        /// Sends ECU Reset (0x11).
        /// </summary>
        public async Task<UdsResponse> EcuResetAsync(
            uint moduleAddress, byte resetType = 0x01, CancellationToken ct = default)
        {
            _log?.Invoke($"ECUReset: Module=0x{moduleAddress:X3}, Type=0x{resetType:X2}");
            var request = new byte[] { 0x11, resetType };
            return await SendWithClassificationAsync(moduleAddress, request, NrcContext.Default, ct);
        }

        #endregion

        #region Internal Methods

        private async Task<UdsResponse> SendWithClassificationAsync(
            uint moduleAddress, byte[] request, NrcContext context, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            var response = await _sendAsync(moduleAddress, request).ConfigureAwait(false);

            // Classify NRC if present
            if (response.HasNrc)
            {
                response.NrcClassification = NrcClassification.FromNrc(response.Nrc!.Value, context);
                _log?.Invoke($"NRC 0x{response.Nrc:X2}: {response.NrcClassification.Description}");
            }

            return response;
        }

        private static string BytesToHex(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return "";
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes)
                sb.AppendFormat("{0:X2}", b);
            return sb.ToString();
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Extracts VIN from response data.
        /// </summary>
        public static string? ExtractVin(byte[]? data)
        {
            // Expected: [0x62, DID_HI, DID_LO, VIN[17 bytes]]
            if (data == null || data.Length < 20) return null;
            return Encoding.ASCII.GetString(data, 3, 17).Trim();
        }

        /// <summary>
        /// Extracts seed from security access response.
        /// </summary>
        public static byte[]? ExtractSeed(byte[]? data)
        {
            // Expected: [0x67, level, seed[n]]
            if (data == null || data.Length < 3) return null;
            var seed = new byte[data.Length - 2];
            Array.Copy(data, 2, seed, 0, seed.Length);
            return seed;
        }

        /// <summary>
        /// Extracts key count from PATS status response.
        /// </summary>
        public static int ExtractKeyCount(byte[]? data)
        {
            // Expected: [0x62, DID_HI, DID_LO, KEY_COUNT, ...]
            if (data == null || data.Length < 4) return 0;
            return data[3];
        }

        /// <summary>
        /// Extracts outcode from security seed response.
        /// </summary>
        public static string? ExtractOutcode(byte[]? data)
        {
            var seed = ExtractSeed(data);
            if (seed == null || seed.Length < 4) return null;
            return BitConverter.ToString(seed, 0, Math.Min(seed.Length, 4)).Replace("-", "");
        }

        /// <summary>
        /// Parses routine status response.
        /// </summary>
        public static RoutineStatusResponse ParseRoutineStatus(byte[]? data)
        {
            // Expected: [0x71, 0x01/0x03, routine_hi, routine_lo, status]
            if (data == null || data.Length < 5)
                return RoutineStatusResponse.Error(0xFF, "Invalid routine response");

            var status = data[4];

            return status switch
            {
                0x00 => RoutineStatusResponse.Pending(),
                0x10 => RoutineStatusResponse.Complete(status, true),
                _ when status >= 0x20 => RoutineStatusResponse.Error(status, $"Routine error: 0x{status:X2}"),
                _ => RoutineStatusResponse.Pending()
            };
        }

        /// <summary>
        /// Converts hex string to bytes.
        /// </summary>
        public static byte[]? HexToBytes(string? hex)
        {
            if (string.IsNullOrEmpty(hex)) return null;
            hex = hex.Replace(" ", "").Replace("-", "").Trim();
            if (hex.Length % 2 != 0) return null;

            var bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            }
            return bytes;
        }

        #endregion
    }

    /// <summary>
    /// UDS response with NRC classification
    /// </summary>
    public sealed class UdsResponse
    {
        public bool Success { get; init; }
        public byte[]? Data { get; init; }
        public byte? Nrc { get; init; }
        public string? ErrorMessage { get; init; }
        public NrcClassification? NrcClassification { get; set; }

        public bool HasNrc => Nrc.HasValue && Nrc.Value != 0;
        public bool IsNegativeResponse => HasNrc;
        public bool IsPositiveResponse => Success && !HasNrc;

        /// <summary>
        /// Checks if response is "Response Pending" (NRC 0x78).
        /// </summary>
        public bool IsResponsePending => Nrc == NrcClassifier.NRC_RESPONSE_PENDING;

        /// <summary>
        /// Gets the error category if this is an NRC response.
        /// </summary>
        public ErrorCategory? ErrorCategory => NrcClassification?.Category;

        /// <summary>
        /// Throws StepException if response indicates failure.
        /// </summary>
        public void ThrowIfFailed(NrcContext context = NrcContext.Default)
        {
            if (!Success)
            {
                if (Nrc.HasValue)
                {
                    throw StepException.FromNrc(Nrc.Value, context);
                }
                throw new StepException(ErrorMessage ?? "UDS request failed", Workflow.ErrorCategory.Unknown);
            }
        }

        public static UdsResponse Ok(byte[]? data = null) =>
            new() { Success = true, Data = data };

        public static UdsResponse Failed(string message, byte? nrc = null) =>
            new() { Success = false, ErrorMessage = message, Nrc = nrc };

        public static UdsResponse FromNrc(byte nrc, NrcContext context = NrcContext.Default) =>
            new()
            {
                Success = false,
                Nrc = nrc,
                NrcClassification = Workflow.NrcClassification.FromNrc(nrc, context),
                ErrorMessage = NrcClassifier.GetDescription(nrc)
            };
    }
}
