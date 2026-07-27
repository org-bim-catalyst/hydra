import * as d3 from "d3";

export default class VizWaveform {

    private svg;

    constructor(_selector) {

        const svg = d3.select(_selector);
        svg.attr('background-color', 'white');
        svg.selectAll('*').remove();

        this.svg = svg;
    }

    public visualize(data) {

        // content area of your visualization
        const vis = this.svg.append('g');
        vis.append('path');

        const margin = {
            top: 0,
            right: 0,
            bottom: 0,
            left: 0
        };

        const width = +this.svg.attr('width') - margin.left - margin.right;

        const height = +this.svg.attr('height') - margin.top - margin.bottom;

        var numberOfPoints = Math.ceil(width / 2);

        var xScale = d3.scaleLinear()
            .range([0, width])
            .domain([0, numberOfPoints]);

        var yScale = d3.scaleLinear()
            .range([height, 0])
            .domain([-1, 1]);

        var line = d3.line()
            .x( (d, i) => { return xScale(i); })
            .y( (d, i) => { return yScale(d); });

        vis.select("path")
            .datum(this.subsample(data, numberOfPoints))
            .attr("d", line)
            .attr('stroke', '#9575CD')
            .attr('fill', 'none');
    }

    subsample(data, numberOfPoints) {
        var subsampledData = new Float32Array(numberOfPoints);

        for (var i = 0; i < numberOfPoints; i++) {
            subsampledData[i] = data[Math.floor(i / numberOfPoints * data.length)];
        }

        console.log(subsampledData);

        return subsampledData;
    }
}