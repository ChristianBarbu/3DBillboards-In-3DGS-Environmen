using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;
using System.Threading;

/// <summary>
/// Source: https://github.com/opentrack/opentrack/discussions/1850
///
/// TODO: This script is very sketchy and we should adjust it - apply Single Responsibility pattern!
/// TODO: Split class into 3 classes: UDP Receiver, Data Parser, PositionRotationUpdater
/// 
/// </summary>
public class UDPReceiver : MonoBehaviour
{
    private UdpClient udpClient;
    private IPEndPoint endPoint;
    private Thread receiveThread;

    // Array for storing received values
    private double[] values;

    // Vector3 for storing position data
    private Vector3 Position;

    /// <summary>
    /// Position's vector x is multiplied by that scalar value.
    /// </summary>
    [SerializeField] private float multiplicatorX;
    
    /// <summary>
    /// Position's vector y is multiplied by that scalar value.
    /// </summary>
    [SerializeField] private float multiplicatorY;
    
    // Vector3 for storing rotation data
    private Vector3 Rotation;

    // GameObject to apply position and rotation data
    public GameObject targetObject;

    public bool useCalibration = false;
    private bool calibrated = false;
    
    
    void Start()
    {
        // Calibration is not really needed, but if for some reason you want to calibrate in Update set useCalibration to true 
        calibrated = !useCalibration;
        
        // Initialize end point with the port number that OpenTrack is sending to
        endPoint = new IPEndPoint(IPAddress.Any, 4242);

        // Initialize UDP client with the end point
        udpClient = new UdpClient(endPoint);

        // Set the receive buffer size
        udpClient.Client.ReceiveBufferSize = 512;

        // Start the receive thread
        receiveThread = new Thread(new ThreadStart(ReceiveData));
        receiveThread.IsBackground = true;
        receiveThread.Start();
    }

    /// <summary>
    /// Once the calibration button is pressed, we reset the position of the target object to Vec3(0,0,0).
    /// </summary>
    public void CalibrateCamera()
    {
        Position = Vector3.zero;
        calibrated = true;
    }
    
    private void ReceiveData()
    {
        while (true)
        {
            try
            {
                // Receive data
                byte[] data = udpClient.Receive(ref endPoint);

                // Check if data length is 48 bytes
                if (data.Length != 48) throw new Exception("Data length is not 48 bytes");

                // Size of each section
                int sectionSize = 8;

                // Initialize values array
                values = new double[data.Length / sectionSize];

                // Number of sections
                int numSections = data.Length / sectionSize;

                // Array for storing sections
                byte[][] sections = new byte[numSections][];

                // Split data into sections
                for (int i = 0; i < numSections; i++)
                {
                    sections[i] = new byte[sectionSize];
                    Buffer.BlockCopy(data, i * sectionSize, sections[i], 0, sectionSize);
                }

                // Convert sections to doubles and assign to Position and Rotation
                for (int i = 0; i < 6; i++)
                {
                    double value = BitConverter.ToDouble(sections[i], 0);
                    values[i] = value;

                    if (i < 3)
                    {
                        // Assign the first three values to Position
                        Position[i] = (float)value / -100;
                    }
                    else
                    {
                        // Switch the y and x rotation
                        if (i == 3)
                        {
                            Rotation.y = (float)value;
                        }
                        else if (i == 4)
                        {
                            Rotation.x = (float)value * -1;
                        }
                        else
                        {
                            Rotation.z = (float)value;
                        }
                    }
                }

              
            }
            catch (Exception e)
            {
                Debug.Log("Error: " + e.ToString());
            }

            // Sleep for 10 milliseconds to reduce the frequency of UDP calls
            Thread.Sleep(10);
        }
    }

    private void OnApplicationQuit()
    {
        // Abort the receive thread
        if (receiveThread != null)
        {
            receiveThread.Abort();
        }

        // Close the UDP client
        udpClient.Close();
    }

    void Update()
    {
        // Apply the position and rotation to the targetObject
        if (targetObject != null && calibrated)
        {
            var zValue = targetObject.transform.localPosition.z;
            var pos = new Vector3(Position.x*multiplicatorX, -Position.y*multiplicatorY, zValue);
            Debug.Log($"Position, UDPReceiver = {pos}");
            targetObject.transform.localPosition = pos;
            //targetObject.transform.localEulerAngles = Rotation;
        }
    }
}
