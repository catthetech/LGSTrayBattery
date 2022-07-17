﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using LGSTrayCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Websocket.Client;

namespace LGSTrayGHUB
{
    public class GHUBDeviceManager : LogiDeviceManager
    {
        private WebsocketClient _ws = null;
        public GHUBDeviceManager(ICollection<LogiDevice> logiDevices) : base(logiDevices)
        {

        }

        private async Task InitialiseWS()
        {
            var url = new Uri("ws://localhost:9010");

            var factory = new Func<ClientWebSocket>(() =>
            {
                var client = new ClientWebSocket();
                client.Options.UseDefaultCredentials = false;
                client.Options.SetRequestHeader("Origin", "file://");
                client.Options.SetRequestHeader("Pragma", "no-cache");
                client.Options.SetRequestHeader("Cache-Control", "no-cache");
                client.Options.SetRequestHeader("Sec-WebSocket-Extensions", "permessage-deflate; client_max_window_bits");
                client.Options.SetRequestHeader("Sec-WebSocket-Protocol", "json");
                client.Options.AddSubProtocol("json");
                return client;
            });

            _ws?.Dispose();
            _ws = new WebsocketClient(url, factory);
            _ws.MessageReceived.Subscribe(ParseSocketMsg);
            _ws.ErrorReconnectTimeout = TimeSpan.FromMilliseconds(500);
            _ws.ReconnectTimeout = null;

            Debug.WriteLine($"Trying to connect to LGHUB_agent, at {url}");

            try
            {
                await _ws.StartOrFail();

            }
            catch (Websocket.Client.Exceptions.WebsocketException)
            {
                Debug.WriteLine("Failed to connect to LGHUB_agent");
                _ws?.Dispose();
                _ws = null;
                return;
            }

            Debug.WriteLine($"Connected to LGHUB_agent");

            _ws.Send(JsonConvert.SerializeObject(new
            {
                msgId = "",
                verb = "SUBSCRIBE",
                path = "/devices/state/changed"
            }));

            _ws.Send(JsonConvert.SerializeObject(new
            {
                msgId = "",
                verb = "SUBSCRIBE",
                path = "/battery/state/changed"
            }));
        }

        private void ParseSocketMsg(ResponseMessage msg)
        {
            GHUBMsg ghubmsg = JsonConvert.DeserializeObject<GHUBMsg>(msg.Text);

            if (ghubmsg.path == "/devices/state/changed")
            {
                LoadDevicesAsync().Wait();
            }
            else if (ghubmsg.path == "/devices/list")
            {
                _loadDevices(ghubmsg.payload);
            }
            //else if (ghubmsg.path.StartsWith("/battery_state/"))
            else if (Regex.IsMatch(ghubmsg.path, @"\/battery\/dev[0-9]+\/state"))
            {
                if (ghubmsg.result["code"]?.ToString() == "SUCCESS")
                {
                    _UpdateDevice(ghubmsg.payload);
                }
                else if (ghubmsg.result["code"]?.ToString() == "NO_SUCH_PATH")
                {
                    _FreezeDevice(ghubmsg);
                }
            }
            else if (ghubmsg.path == "/battery/state/changed")
            {
                _UpdateDevice(ghubmsg.payload);
            }
            else
            {
            }
            //Debug.WriteLine(msg);
        }
        public override async Task LoadDevicesAsync()
        {
            if (_ws == null)
            {
                // Check if LGHUB_agent is back
                await InitialiseWS();
            }

            if (_ws == null)
            {
                // LGHUB_agent is not back
                return;
            }

            _ws.Send(JsonConvert.SerializeObject(new
            {
                msgId = "",
                verb = "GET",
                path = "/devices/list"
            }));
        }

        private void _loadDevices(JObject payload)
        {
            _LogiDevices.Clear();

            foreach (var deviceToken in payload["deviceInfos"])
            {
                if (!Enum.TryParse(deviceToken["deviceType"].ToString(), true, out DeviceType deviceType))
                {
                    deviceType = DeviceType.Mouse;
                }

                LogiDeviceGHUB device = new LogiDeviceGHUB()
                {
                    DeviceID = deviceToken["id"].ToString(),
                    DeviceName = deviceToken["extendedDisplayName"].ToString(),
                    DeviceType = deviceType
                };

                _LogiDevices.Add(device);
            }

            UpdateDevicesAsync().Wait();
        }

        public override async Task UpdateDevicesAsync()
        {
            foreach (var device in _LogiDevices.Select(x => x as LogiDeviceGHUB).Where(x => x.BatteryStatExpired && x.HasBattery))
            {
                _ws.Send(JsonConvert.SerializeObject(new
                {
                    msgId = "",
                    verb = "GET",
                    path = $"/battery/{device.DeviceID}/state"
                }));
            }

            await Task.Delay(0).ConfigureAwait(false);
        }

        private void _UpdateDevice(JObject payload)
        {
            LogiDeviceGHUB device = _LogiDevices.FirstOrDefault(x => x.DeviceID == payload["deviceId"].ToString()) as LogiDeviceGHUB;

            if (device == null)
            {
                return;
            }

            device.BatteryPercentage = payload["percentage"].ToObject<double>();
            device.Mileage = payload["mileage"].ToObject<double>();
            device.Charging = payload["charging"].ToObject<bool>();
        }

        private void _FreezeDevice(GHUBMsg msg)
        {
            var match = Regex.Match(msg.path, ".+?(dev[0-9]+).+");
            LogiDeviceGHUB device = _LogiDevices.FirstOrDefault(x => x.DeviceID == match.Groups[1].Value) as LogiDeviceGHUB;

            if (device == null)
            {
                return;
            }

            device.HasBattery = false;
        }
    }
}
