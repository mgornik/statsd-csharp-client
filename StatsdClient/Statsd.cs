﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace StatsdClient
{
  /// <summary>
  /// The statsd client library.
  /// </summary>
  public class Statsd : IStatsd
  {
    private string _prefix;
    private IOutputChannel _outputChannel;
    private Func<string, string, string> _nameSourceConcat;

    /// <summary>
    /// Creates a new instance of the Statsd client.
    /// </summary>
    /// <param name="host">The statsd or statsd.net server.</param>
    /// <param name="port"></param>
    public Statsd(string host, int port)
    {
      if ( String.IsNullOrEmpty( host ) )
      {
        Trace.TraceWarning( "Statsd client initialised with empty host address. Dropping back to NullOutputChannel." );
        InitialiseInternal( () => new NullOutputChannel(), "", false, null );
      }
      else
      {
        InitialiseInternal( () => new UdpOutputChannel( host, port ), "", false, null );
      }
    }

    /// <summary>
    /// Function that concatenates name and source using following pattern: source|name
    /// </summary>
    /// <param name="name">Name of the metric</param>
    /// <param name="source">Source which generated this metric</param>
    /// <returns>Resulting string</returns>
    public string ConcatNameAndSourceWithPipe(string name, string source)
    {
      return string.Format("{0}|{1}", source, name);
    }

    /// <summary>
    /// Creates a new instance of the Statsd client.
    /// </summary>
    /// <param name="host">The statsd or statsd.net server.</param>
    /// <param name="port"></param>
    /// <param name="prefix">A string prefix to prepend to every metric.</param>
    /// <param name="nameSourceConcat">Function that gets metric name and source and produces full name</param>
    /// <param name="rethrowOnError">If True, rethrows any exceptions caught due to bad configuration.</param>
    /// <param name="connectionType">Choose between a UDP (recommended) or TCP connection.</param>
    /// <param name="retryOnDisconnect">Retry the connection if it fails (TCP only).</param>
    /// <param name="retryAttempts">Number of times to retry before giving up (TCP only).</param>
    public Statsd(string host, 
      int port, 
      ConnectionType connectionType = ConnectionType.Udp, 
      string prefix = null,
      Func<string, string, string> nameSourceConcat = null, 
      bool rethrowOnError = false,
      bool retryOnDisconnect = true,
      int retryAttempts = 3)
    {
      InitialiseInternal(() =>
        {
          return connectionType == ConnectionType.Tcp
        ? (IOutputChannel)new TcpOutputChannel(host, port, retryOnDisconnect, retryAttempts)
        : (IOutputChannel)new UdpOutputChannel(host, port);
        },
        prefix,
        rethrowOnError,
        nameSourceConcat);
    }

    /// <summary>
    /// Creates a new instance of the Statsd client.
    /// </summary>
    /// <param name="host">The statsd or statsd.net server.</param>
    /// <param name="port"></param>
    /// <param name="prefix">A string prefix to prepend to every metric.</param>
    /// <param name="rethrowOnError">If True, rethrows any exceptions caught due to bad configuration.</param>
    /// <param name="outputChannel">Optional output channel (useful for mocking / testing).</param>
    public Statsd(string host, int port, string prefix = null, bool rethrowOnError = false, IOutputChannel outputChannel = null)
    {
      if (outputChannel == null)
      {
        InitialiseInternal(() => new UdpOutputChannel(host, port), prefix, rethrowOnError, null);
      }
      else
      {
        InitialiseInternal(() => outputChannel, prefix, rethrowOnError, null);
      }
    }

    private void InitialiseInternal(Func<IOutputChannel> createOutputChannel, string prefix, bool rethrowOnError, Func<string, string, string> nameSourceConcat)
    {
      _prefix = prefix;
      _nameSourceConcat = nameSourceConcat;
      if (_prefix != null && _prefix.EndsWith("."))
      {
        _prefix = _prefix.Substring(0, _prefix.Length - 1);
      }
      try
      {
        _outputChannel = createOutputChannel();
      }
      catch (Exception ex)
      {
        if (rethrowOnError)
        {
          throw;
        }
        Trace.TraceError("Could not initialise the Statsd client: {0} - falling back to NullOutputChannel.", ex.Message);
        _outputChannel = new NullOutputChannel();
      }
    }

    /// <summary>
    /// Log a counter.
    /// </summary>
    /// <param name="name">The metric name.</param>
    /// <param name="count">The counter value (defaults to 1).</param>
    /// <param name="source">Source which generated this metric</param>
    public void LogCount(string name, int count = 1, string source = null)
    {
      SendMetric(MetricType.COUNT, name, source, _prefix, count);
    }

    /// <summary>
    /// Log a timing / latency
    /// </summary>
    /// <param name="name">The metric name.</param>
    /// <param name="milliseconds">The duration, in milliseconds, for this metric.</param>
    /// <param name="source">Source which generated this metric</param>
    public void LogTiming(string name, int milliseconds, string source = null)
    {
      SendMetric(MetricType.TIMING, name, source, _prefix, milliseconds);
    }

    /// <summary>
    /// Log a timing / latency
    /// </summary>
    /// <param name="name">The metric name.</param>
    /// <param name="milliseconds">The duration, in milliseconds, for this metric.</param>
    /// <param name="source">Source which generated this metric</param>
    public void LogTiming(string name, long milliseconds, string source = null)
    {
      LogTiming(name, (int)milliseconds, source);
    }

    /// <summary>
    /// Log a gauge.
    /// </summary>
    /// <param name="name">The metric name</param>
    /// <param name="value">The value for this gauge</param>
    /// <param name="source">Source which generated this metric</param>
    public void LogGauge(string name, int value, string source = null)
    {
      SendMetric(MetricType.GAUGE, name, source, _prefix, value);
    }

    /// <summary>
    /// Log to a set
    /// </summary>
    /// <param name="name">The metric name.</param>
    /// <param name="value">The value to log.</param>
    /// <param name="source">Source which generated this metric</param>
    /// <remarks>Logging to a set is about counting the number
    /// of occurrences of each event.</remarks>
    public void LogSet(string name, int value, string source = null)
    {
      SendMetric(MetricType.SET, name, source, _prefix, value);
    }

    /// <summary>
    /// Log a calendargram metric 
    /// </summary>
    /// <param name="name">The metric namespace</param>
    /// <param name="value">The unique value to be counted in the time period</param>
    /// <param name="period">The time period, can be one of h,d,dow,w,m</param>
    /// <param name="source">Source which generated this metric</param>
    public void LogCalendargram(string name, string value, string period, string source = null)
    {
      SendMetric(MetricType.CALENDARGRAM, name, source, _prefix, value, period);
    }

    /// <summary>
    /// Log a calendargram metric 
    /// </summary>
    /// <param name="name">The metric namespace</param>
    /// <param name="value">The unique value to be counted in the time period</param>
    /// <param name="period">The time period, can be one of h,d,dow,w,m</param>
    /// <param name="source">Source which generated this metric</param>
    public void LogCalendargram(string name, int value, string period, string source = null)
    {
      SendMetric(MetricType.CALENDARGRAM, name, source, _prefix, value, period);
    }

    /// <summary>
    /// Log a raw metric that will not get aggregated on the server.
    /// </summary>
    /// <param name="name">The metric name.</param>
    /// <param name="value">The metric value.</param>
    /// <param name="epoch">(optional) The epoch timestamp. Leave this blank to have the server assign an epoch for you.</param>
    public void LogRaw(string name, int value, long? epoch = null, string source = null)
    {
      SendMetric(MetricType.RAW, name, source, String.Empty, value, epoch.HasValue ? epoch.ToString() : (string)null);
    }

    private void SendMetric(string metricType, string name, string source, string prefix, int value, string postFix = null)
    {
      if (value < 0)
      {
          Trace.TraceWarning(String.Format("Metric value for {0} was less than zero: {1}. Not sending.", name, value));
          return;
      }
      SendMetric(metricType, name, source, prefix, value.ToString(), postFix);
    }

    private void SendMetric(string metricType, string name, string source, string prefix, string value, string postFix = null)
    {
      if (String.IsNullOrEmpty(name))
      {
        throw new ArgumentNullException("name");
      }
      _outputChannel.Send(PrepareMetric(metricType, name, source, prefix, value, postFix));
    }

    /// <summary>
    /// Prepare a metric prior to sending it off ot the Graphite server.
    /// </summary>
    /// <param name="metricType"></param>
    /// <param name="name"></param>
    /// <param name="prefix"></param>
    /// <param name="value"></param>
    /// <param name="postFix">A value to append to the end of the line.</param>
    /// <returns>The formatted metric</returns>
    protected virtual string PrepareMetric(string metricType, string name, string source, string prefix, string value, string postFix = null)
    {
      var nameText = name;
      if (!String.IsNullOrEmpty(source))
      {
        nameText = _nameSourceConcat != null
          ? _nameSourceConcat(name, source)
          : ConcatNameAndSourceWithPipe(name, source);
      }
      return (String.IsNullOrEmpty(prefix) ? nameText : (prefix + "." + nameText))
        + ":" + value
        + "|" + metricType
        + (postFix == null ? String.Empty : "|" + postFix);
    }
  }
}
