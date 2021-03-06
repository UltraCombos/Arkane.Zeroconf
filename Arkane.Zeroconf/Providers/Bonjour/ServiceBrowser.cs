#region header

// Arkane.ZeroConf - ServiceBrowser.cs
// 

#endregion

#region using

using System ;
using System.Collections ;
using System.Collections.Generic ;
using System.Threading.Tasks ;

#endregion

namespace ArkaneSystems.Arkane.Zeroconf.Providers.Bonjour
{
    public class ServiceBrowseEventArgs : Arkane.Zeroconf.ServiceBrowseEventArgs
    {
        public ServiceBrowseEventArgs (BrowseService service, bool moreComing) : base (service) => this.MoreComing = moreComing ;

        public bool MoreComing { get ; }
    }

    public class ServiceBrowser : IServiceBrowser, IDisposable
    {
        public ServiceBrowser () => this.browseReplyHandler = this.OnBrowseReply ;

        private readonly Native.DNSServiceBrowseReply            browseReplyHandler ;
        private readonly Dictionary <string, IResolvableService> serviceTable = new Dictionary <string, IResolvableService> () ;

        private AddressProtocol address_protocol ;
        private string          domain ;
        private uint            interfaceIndex ;
        private string          regtype ;

        private ServiceRef sdRef = ServiceRef.Zero ;

        private Task task ;

        public event ServiceBrowseEventHandler ServiceAdded ;

        public event ServiceBrowseEventHandler ServiceRemoved ;

        public void Browse (uint interfaceIndex, AddressProtocol addressProtocol, string regtype, string domain)
        {
            this.Configure (interfaceIndex, addressProtocol, regtype, domain) ;
            this.StartAsync () ;
        }

        public void Dispose () { this.Stop () ; }

        public IEnumerator <IResolvableService> GetEnumerator ()
        {
            lock (this)
            {
                foreach (IResolvableService service in this.serviceTable.Values)
                    yield return service ;
            }
        }

        IEnumerator IEnumerable.GetEnumerator () => this.GetEnumerator () ;

        public void Configure (uint interfaceIndex, AddressProtocol addressProtocol, string regtype, string domain)
        {
            this.interfaceIndex   = interfaceIndex ;
            this.address_protocol = addressProtocol ;
            this.regtype          = regtype ;
            this.domain           = domain ;

            if (regtype == null)
                throw new ArgumentNullException ("regtype") ;
        }

        private void Start (bool async)
        {
            if (this.task != null)
                throw new InvalidOperationException ("ServiceBrowser is already started") ;

            if (async)
                this.task = Task.Run (() => this.ProcessStart ()).ContinueWith (_ => this.task = null) ;
            else
                this.ProcessStart () ;
        }

        public void Start () { this.Start (false) ; }

        public void StartAsync () { this.Start (true) ; }

        private void ProcessStart ()
        {
            ServiceError error = Native.DNSServiceBrowse (out this.sdRef,
                                                          ServiceFlags.Default,
                                                          this.interfaceIndex,
                                                          this.regtype,
                                                          this.domain,
                                                          this.browseReplyHandler,
                                                          IntPtr.Zero) ;

            if (error != ServiceError.NoError)
                throw new ServiceErrorException (error) ;

            this.sdRef.Process () ;
        }

        public void Stop ()
        {
            if (this.sdRef != ServiceRef.Zero)
            {
                this.sdRef.Deallocate () ;
                this.sdRef = ServiceRef.Zero ;
            }

            this.task?.Wait () ;
        }

        private void OnBrowseReply (ServiceRef   sdRef,
                                    ServiceFlags flags,
                                    uint         interfaceIndex,
                                    ServiceError errorCode,
                                    IntPtr       serviceName,
                                    string       regtype,
                                    string       replyDomain,
                                    IntPtr       context)
        {
            string name = Native.Utf8toString (serviceName) ;

            var service = new BrowseService () ;
            service.Flags           = flags ;
            service.Name            = name ;
            service.RegType         = regtype ;
            service.ReplyDomain     = replyDomain ;
            service.InterfaceIndex  = interfaceIndex ;
            service.AddressProtocol = this.address_protocol ;

            var args = new ServiceBrowseEventArgs (
                                                   service,
                                                   (flags & ServiceFlags.MoreComing) != 0) ;

            if ((flags & ServiceFlags.Add) != 0)
            {
                lock (this.serviceTable)
                {
                    if (this.serviceTable.ContainsKey (name))
                        this.serviceTable[name] = service ;
                    else
                        this.serviceTable.Add (name, service) ;
                }

                ServiceBrowseEventHandler handler = this.ServiceAdded ;
                handler?.Invoke (this, args) ;
            }
            else
            {
                lock (this.serviceTable)
                {
                    if (this.serviceTable.ContainsKey (name))
                        this.serviceTable.Remove (name) ;
                }

                ServiceBrowseEventHandler handler = this.ServiceRemoved ;
                handler?.Invoke (this, args) ;
            }
        }
    }
}
