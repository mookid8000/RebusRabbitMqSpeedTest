# Very simple Rebus-on-RabbitMQ speed test

Performs the following sequence of operations:

1. Start Rebus endpoint with 0 workers (i.e. it does not receive anything)
1. Start create `ConcurrentQueue` of messages to be sent
1. Start a number of threads and measure time spent sending messages (`'Sending messages'` action)
1. Start a number of workers (i.e. start receiving messages)
1. Measure time spent waiting for all messages to be received (`'Receive messages'` action)

# Results

Unscientifically, and probably different on your machine. On my machine I got this:

    Action 'Sending messages' executing...
    Action 'Sending messages' took 5,4675042 - that's 1829 /s
    Action 'Received messages' executing...
    Action 'Received messages' took 1,5385085 - that's 6499,8 /s

My machine is running Windows 10 in a VMWare Fusion virtual machine on a 2013 Macbook Pro (2.7 GHz Intel Core I7) with 16 GB of RAM.